# Contributing to StepLoop

Thanks for your interest! StepLoop is a small personal project, and bug reports, ideas and pull requests are welcome.
You can write in **English or Italian** (inglese o italiano).

## Reporting a bug or asking for a feature

- Use the [issue templates](https://github.com/Kingly26/StepLoop/issues/new/choose): *Bug report* or *Feature request*.
- For a bug, please include the StepLoop version (the Release you downloaded, or the *File version* in the exe's
  *Properties → Details*), your Windows version and, if you can, the profile (`.json`) that shows the problem.
  Remove anything private from it first.
- Security problems: **do not open a public issue**, see [SECURITY.md](SECURITY.md).
- Out of scope: help with bypassing, hiding from or avoiding anti-cheat systems, and questions about bans. The
  [README](../README.md) explains the risks of using automation in online games.

## Building

You only need Windows with **.NET Framework 4.x** (already included in Windows 10/11). There are no external dependencies
and no SDK to install.

```bat
build.bat
```

This runs the C# compiler (`csc.exe`, C# 5) on `src\*.cs` and produces `StepLoop.exe` next to the script.

## Code guidelines

- **C# 5 only** (the compiler that ships with .NET Framework 4.x): no string interpolation (`$""`), no `?.`, no
  expression-bodied members, no `nameof`, no auto-property initializers.
- **No external dependencies**: the app stays a single `.exe` built from `src\*.cs`.
- **Bilingual UI**: every user-visible string goes through `L.T("italiano", "english")`, so both languages stay complete.
- **Custom-drawn controls** follow the palette in `Pal` (dark and light themes). Use the theme tokens instead of fixed colors, and
  check both themes.
- **Profiles are JSON** (`DataContractJsonSerializer`). New fields must be optional and keep old profiles loading
  (`EmitDefaultValue = false` for new members, and never renumber the values of `StepType`).
- Keep names, file names and new comments in **English**. (Older comments in `src\` are still in Italian.)

## Testing your change

The repository does not ship an automated test suite yet, so please check your change by hand:

1. Build with `build.bat` and start the app.
2. Use the **Simulation** option: it runs the sequence without sending any real keyboard or mouse input.
3. Try the sample profiles in `profiles\` (open them, run them in Simulation, save and reload).
4. If you touched the interface, check the dark and light themes and both languages (IT/EN button), and resize the sections.

## Pull requests

- Keep each pull request focused on one change.
- Describe what changed and why, and how you tested it (the pull request template has a checklist).
- By contributing you agree that your work is released under the project's [MIT license](../LICENSE).
