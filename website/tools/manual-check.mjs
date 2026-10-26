// `pnpm manual:check`: what the manual still lacks. Lists the blocks with no page, and per block the knobs (and
// choices) with no description, the descriptions for knobs the engine does not have, the chapters in
// guide/chapters.ts with no Markdown and the Markdown files no chapter lists. Exits 0 with a summary; with --strict it
// exits 1 when anything is missing.
//
// It loads the TypeScript sources themselves (through typescript's transpiler), so it reads exactly what the pages do.

import { existsSync, readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import ts from 'typescript';
import { writeIndex } from './manual-index.mjs';

const strict = process.argv.includes('--strict');
const verbose = process.argv.includes('--verbose');
const manual = join(dirname(fileURLToPath(import.meta.url)), '..', 'src', 'app', 'manual');

// Extensionless relative imports resolve to .ts files, .ts is transpiled, and .md loads as text, as the build does.
registerHooks({
  resolve(specifier, context, next) {
    if (specifier.startsWith('.') && context.parentURL?.startsWith('file:') && !/\.[cm]?[jt]s$|\.md$/.test(specifier)) {
      const base = fileURLToPath(new URL(specifier, context.parentURL));
      for (const candidate of [`${base}.ts`, join(base, 'index.ts')]) {
        if (existsSync(candidate)) {
          return next(pathToFileURL(candidate).href, context);
        }
      }
    }
    return next(specifier, context);
  },
  load(url, context, next) {
    if (url.endsWith('.md')) {
      return {
        format: 'module',
        source: `export default ${JSON.stringify(readFileSync(new URL(url), 'utf8'))};`,
        shortCircuit: true
      };
    }
    if (url.endsWith('.ts')) {
      const { outputText } = ts.transpileModule(readFileSync(new URL(url), 'utf8'), {
        fileName: fileURLToPath(url),
        compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 }
      });
      return { format: 'module', source: outputText, shortCircuit: true };
    }
    return next(url, context);
  }
});

const { chapters: mdFiles } = writeIndex();
const { BLOCK_DOCS, CHAPTERS } = await import(pathToFileURL(join(manual, 'content.generated.ts')).href);
const { ENGINE_BLOCKS } = await import(pathToFileURL(join(manual, 'engine-params.generated.ts')).href);
const { describeOption, describeParam, patternOf } = await import(pathToFileURL(join(manual, 'describe.ts')).href);

const placeholder = text => !text.replace(/<!--[\s\S]*?-->/g, '').trim();
const problems = { blocks: 0, params: 0, options: 0, stale: 0, chapters: 0, orphans: 0 };
const out = [];

// ------------------------------------------------------------------ blocks
const engineIds = new Set(ENGINE_BLOCKS.map(block => block.typeId));

for (const block of ENGINE_BLOCKS) {
  const doc = BLOCK_DOCS[block.typeId];

  if (!doc || (!doc.summary?.trim() && !Object.keys(doc.params ?? {}).length)) {
    problems.blocks++;
    out.push(
      `✗ ${block.typeId}: ${doc ? 'placeholder page (no summary, no knobs)' : 'no page'} (blocks/${block.typeId}.ts)`
    );
    problems.params += block.params.length;
    continue;
  }

  const lines = [];
  const missing = block.params.filter(param => !describeParam(doc, param.id)).map(param => param.id);

  if (!doc.summary?.trim()) {
    lines.push('  no summary');
  }

  if (missing.length) {
    problems.params += missing.length;
    lines.push(`  ${missing.length} of ${block.params.length} knobs undescribed: ${missing.join(', ')}`);
  }

  const optionsMissing = verbose
    ? block.params
        .filter(
          param =>
            param.kind === 'choice' &&
            doc.options &&
            Object.keys(doc.options).some(key => patternOf(key).test(param.id))
        )
        .flatMap(param =>
          (param.options ?? [])
            .filter(option => !describeOption(doc, param.id, option))
            .map(option => `${param.id}:${option}`)
        )
    : [];

  if (optionsMissing.length) {
    problems.options += optionsMissing.length;
    lines.push(`  choices undescribed: ${optionsMissing.join(', ')}`);
  }

  // Keys that match no knob are text no page shows: a typo, or a knob the engine renamed.
  const ids = block.params.map(param => param.id);
  const optionNames = new Map(block.params.map(param => [param.id, param.options ?? []]));
  const stale = Object.keys(doc.params ?? {}).filter(key => !ids.some(id => patternOf(key).test(id)));

  for (const [key, options] of Object.entries(doc.options ?? {})) {
    const params = ids.filter(id => patternOf(key).test(id));
    if (!params.length) {
      stale.push(`options.${key}`);
      continue;
    }
    for (const option of Object.keys(options)) {
      if (!params.some(id => optionNames.get(id).includes(option))) {
        stale.push(`options.${key}["${option}"]`);
      }
    }
  }

  if (stale.length) {
    problems.stale += stale.length;
    lines.push(`  text for knobs or choices the engine does not have: ${stale.join(', ')}`);
  }

  out.push(
    `${lines.length ? '✗' : '✓'} ${block.typeId}${lines.length ? '' : `: all ${block.params.length} knobs described`}`
  );
  out.push(...lines);
}

for (const typeId of Object.keys(BLOCK_DOCS).filter(id => !engineIds.has(id))) {
  problems.stale++;
  out.push(`✗ blocks/${typeId}.ts: the engine has no block '${typeId}'`);
}

// ------------------------------------------------------------------ chapters
out.push('');
const slugs = new Set(CHAPTERS.map(chapter => chapter.slug));

for (const chapter of CHAPTERS) {
  const path = join(manual, 'guide', `${chapter.slug}.md`);
  if (!existsSync(path) || placeholder(readFileSync(path, 'utf8'))) {
    problems.chapters++;
    out.push(`✗ chapter '${chapter.slug}': ${existsSync(path) ? 'placeholder' : 'no'} guide/${chapter.slug}.md`);
  }
}

for (const file of mdFiles.filter(name => !slugs.has(name))) {
  problems.orphans++;
  out.push(`✗ guide/${file}.md: no chapter in guide/chapters.ts lists it`);
}

console.log(out.join('\n'));

const knobs = ENGINE_BLOCKS.reduce((sum, block) => sum + block.params.length, 0);
console.log(
  [
    '',
    `Blocks:   ${ENGINE_BLOCKS.length - problems.blocks} of ${ENGINE_BLOCKS.length} have a page`,
    `Knobs:    ${knobs - problems.params} of ${knobs} described`,
    ...(verbose ? [`Choices:  ${problems.options} undescribed in blocks that describe choices`] : []),
    `Chapters: ${CHAPTERS.length - problems.chapters} of ${CHAPTERS.length} written; ${problems.orphans} Markdown files no chapter lists`,
    `Stale:    ${problems.stale} descriptions for knobs, choices or blocks the engine does not have`
  ].join('\n')
);

const total =
  problems.blocks + problems.params + problems.chapters + problems.orphans + problems.stale + problems.options;
if (strict && total > 0) {
  process.exit(1);
}
