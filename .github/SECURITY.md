# Security policy

## Supported versions

Only the **latest release** receives fixes. Please check that you can reproduce the problem on the
[latest release](https://github.com/Kingly26/StepLoop/releases/latest) before reporting.

## Reporting a vulnerability

**Please do not open a public issue for a security problem.**

Use GitHub's private reporting instead: open the **Security** tab of the repository and choose
**Report a vulnerability** ([direct link](https://github.com/Kingly26/StepLoop/security/advisories/new)).

Please include:

- the StepLoop version and your Windows version;
- what you found and the steps to reproduce it (a small profile `.json` helps, with anything private removed);
- what you think the impact is.

This is a personal project maintained in spare time, so replies are best effort. I will acknowledge a report as soon as I can,
and credit you in the release notes if you wish.

## What StepLoop does (for context)

- It has **no network code**: no telemetry, no updates, nothing is downloaded or uploaded.
- It **sends keyboard and mouse input** (Windows `SendInput`) while a sequence runs, and **reads the color of screen pixels**
  for color conditions. It registers global hotkeys (F8/F7 by default).
- While you use **REC (recording)** it installs low-level keyboard and mouse hooks, only for the duration of the recording.
  Keys are stored only if you enable *Also record the keys pressed*. Do not type passwords or other private text while
  recording, and review a recorded profile before sharing it.
- It stores its settings and the last profile in `%AppData%\StepLoop`. Profiles are plain JSON files.
- The released `StepLoop.exe` is **not digitally signed**. Each release lists the SHA-256 of the file in its notes, so you can
  check that what you downloaded matches (`Get-FileHash StepLoop.exe` in PowerShell).

## Out of scope

- Detection or bans by anti-cheat systems, and help with avoiding them (see the warning in the [README](../README.md)).
- Antivirus or SmartScreen warnings caused by the file being unsigned and sending input: please open a normal issue if a
  scanner flags a release.
