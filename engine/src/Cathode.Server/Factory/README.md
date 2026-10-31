# The factory bank

The factory presets, built into the Server (`Stores/FactoryPresets.cs`) as embedded resources, so they ship inside
the desktop app and the headless package with no files of their own.

- `presets/<name>.cthpreset` — one preset each, a package as the editor exports it (no IRs or captures: a factory
  preset uses none). The file name is the preset's id after `factory-` (`brown-sound.cthpreset` is
  `factory-brown-sound`). The id, name in the manifest, author and factory flag inside are replaced or ignored when the
  bank is read, and the user's colour and favourite are kept apart, so they do not matter here.
- `bank.json` — the bank's order, one entry per file, with each planned scene's lift in dB over the bank's loudness
  (a lead scene's few dB more). The scenes after the last lift are spares. A file it does not list is not loaded.

## Changing a preset

1. Load it in the editor, change it, and export it (Library ▸ Export, a `.cthpreset`).
2. Copy the exported file over `presets/<name>.cthpreset`, keeping the name.
3. Level the bank, so every scene plays at −18 LUFS plus its lift (this rewrites the amp and scene levels in the
   packages, with the preset's JSON indented and the entries dated, so they compare well):

   ```sh
   cd engine
   dotnet build -c Release
   CATHODE_UPDATE_FACTORY_LEVELS=1 dotnet test --project tests/Cathode.Server.Tests -c Release --no-build --filter-class "*FactoryPresetLevelTests"
   ```

4. Rebuild and run the tests (`FactoryPresetTests`, `FactoryPresetLevelTests`): they check every file loads with
   known blocks and in-range values, fits the CPU budget, keeps pedals in front of the amp, and plays at the bank's
   loudness without clipping.

## Adding or removing a preset

Add or remove its file, and its entry in `bank.json` (with as many lifts as it has planned scenes), then level and
test as above. The bank holds at most one bank's slots.
