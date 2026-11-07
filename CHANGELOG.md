# Changelog

## 1.4.2 — Input number grouping

- Show automatic grouping commas in decimal inputs as you type, including variable assignments and function arguments.
- Commas are visual adornments: formulas, saved notes, clipboard text, cursor positions and undo history stay unchanged.
- Keep comments, quoted variable names, fractional digits, hexadecimal/binary numbers and scientific notation unchanged.
- Includes grouped answers and variable/function Tab completion.

## 1.4.1 — Readable large numbers

- Automatically group displayed answers and variable values with commas: `123,456,789`.
- Preserve decimal digits, scientific notation and calculation precision.
- Copy results without grouping so they remain valid in formulas and function arguments.
- Includes variable and function autocomplete from 1.4.0: Tab to accept, Up/Down to choose, Ctrl+Space to show suggestions.

## 1.4.0 — Tab completion

- Suggest variable names, constants and calculator functions as you type.
- Press Tab to accept, Up/Down to choose, Ctrl+Space to show suggestions, or Esc to dismiss.
- Functions add parentheses and place the cursor inside; existing arguments are preserved.
- Suggestions respect note scope, both variable syntaxes, comments and quoted names.
- Completion supports undo and wrapped text. Enter and unmatched Tab retain their normal behaviour.

## 1.3.0 — Optional text wrapping

- Added Settings → Wrap text, saved independently for each note (off by default).
- Wrapped calculations keep their inline answers and logical line numbers when resized or scrolled.
- Copy-result and clear commands respect logical lines across visual wraps.
- Long error messages fit the viewport with full detail in tooltips.
- Existing notes and their source text are preserved.

## 1.2.2 — Variables inside calculations

- Quoted definitions now work anywhere in a calculation: `3"teabage"+2=` gives 5 and defines teabage as 3.
- Supports multiple bindings, function arguments and reuse later in the same expression.
- Quoted names bind the preceding value; use parentheses to name an entire calculation.
- Failed lines discard partial bindings and invalidate attempted reassignments.

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
