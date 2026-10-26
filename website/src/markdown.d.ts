// The manual's chapters are Markdown, imported as text (angular.json's `loader` for `.md`).
declare module '*.md' {
  const text: string;
  export default text;
}
