# Accessibility statement

StepLoop is a small desktop app and I want it to be usable by as many people as possible. This page describes what works today,
what does not, and how to report a barrier. It is an honest status, not a certification: the app has **not** been formally
audited.

## Supported environments

- Windows 10 and Windows 11, with .NET Framework 4.x (included in Windows).
- High-DPI displays (the app is DPI-aware and scales its interface).
- Dark and light themes, switchable from the bottom bar.
- Interface languages: English and Italian.

## What works

- **Keyboard shortcuts:** start/stop (default `F8`) and pause (default `F7`) are global hotkeys and can be changed.
- **Step list:** `↑` `↓` `Home` `End` `PageUp` `PageDown` move the selection, `←` `→` collapse and expand blocks, `Delete` removes the step,
  `Ctrl+D` duplicates it, `Alt+↑` / `Alt+↓` move it.
- **Adding blocks without dragging:** double-click a block in the palette to add it, instead of dragging it.
- **Not color alone:** block families have text badges (KEYS, IF, REPEAT, …) in addition to their colors, and states are also
  written as text (for example "Stopped" / "Running").
- **Readable text:** the text of the interface follows the Windows display scaling.

## Known limitations

- **Screen readers:** the interface uses custom-drawn controls that do not provide the usual accessibility names and roles, so
  a screen reader (Narrator, NVDA, JAWS) is likely to announce little. This has **not been tested** and should be assumed to be poor.
- **Mouse-oriented tasks:** picking a screen area or color ("Pick from the screen", F9) and recording clicks need a pointing device.
  Coordinates can also be typed by hand.
- **High contrast mode** and Windows text-size settings are not followed; the app uses its own dark/light themes.
- **Fixed layout:** very small windows or very large text scaling can make the side panels scroll or cut some labels.
- **Tooltips:** some explanations are only available as tooltips on hover.

## Reporting an accessibility barrier

Please open an [issue](https://github.com/Kingly26/StepLoop/issues/new/choose) (English or Italian) describing:

- what you were trying to do and what got in the way;
- the assistive technology or settings you use, and your Windows version;
- what would have helped, if you know.

Accessibility reports are treated like bugs and prioritized with the other fixes.
