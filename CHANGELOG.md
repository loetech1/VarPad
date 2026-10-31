# Changelog

## 1.2.1 — Delete notes

- Added Notes → Delete this note, with confirmation before permanent deletion.
- Removes the saved note, backup and temporary file without autosave recreating them.
- Deleting the last open note opens another saved note, or a blank scratchpad.
- Closing a window still keeps its note saved.

## 1.2.0 — VarPad

- Renamed the application, installer, shortcuts and executable to VarPad.
- Added value-first variable assignment: `25"horses"`, alongside `horses = 25`.
- Supports expression bindings such as `(20 + 5)"herd"`, curly quotes, inline comments and case-insensitive names.
- Imports previous StickierCalc notes and backups once, keeping originals intact.
- Published example screenshots and reproducible screenshot tooling.

## 1.1.0 — StickierCalc

- Added `cls` and `clear` followed by Enter, with undo support.
- Compacted the header and toolbar into a single row.
- Packaged a per-user Windows installer.

## 1.0.0 — StickierCalc

- Initial native Windows notepad calculator with scientific functions and variables.
- Inline answers, multiple notes, autosave and themes.
