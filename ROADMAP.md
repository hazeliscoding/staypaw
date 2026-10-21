# Roadmap

Staypaw is a Windows tray app that stops other apps from taking your keyboard focus. This file tracks the decisions already made, what gets built in what order, and what's out of scope.

## Architecture characteristics

What the design must protect. Everything else gets ordinary good design.

1. **Speed.** Focus comes back before you notice, and no key you type during a steal reaches the stealer.
2. **Trust.** Staypaw watches the keyboard, so nothing is injected, nothing goes over the network, no key is stored, and builds are signed.
3. **Footprint.** It idles all day beside a game, so it uses close to no CPU and little memory while nothing happens.

## Decisions (2026-10-21)

- **Build Staypaw** instead of contributing to NoFocusSteal. Protecting a picked app, holding keys and signed winget installs change its design, and it has one maintainer who has been quiet since 2026-09-28. This gives up a head start inside an existing project, and the longer log run that was meant to come first: the first run caught no game data.
- **Speed, trust and footprint** are the characteristics above. Compatibility with admin apps and exclusive-fullscreen games comes second.
- **Public, under Apache-2.0.** A keyboard watcher is easier to trust when people can read it. This gives up a closed-source paid app; the paid layer is still open.
- **.NET 10, Avalonia 12 and NativeAOT,** laid out like Gil Sweep: `Staypaw.Core` (the watcher and the rules, no UI), `Staypaw.Desktop` (the tray app) and `Staypaw.Tests`. Win32 calls go through CsWin32. This gives up NoFocusSteal's ~100 KB exe on .NET Framework 4.8; a NativeAOT exe needs no runtime either, but it's bigger.
- **Port NoFocusSteal's watcher, write our own rules.** The WinEvent hooks, the 15 ms foreground poll, the Raw Input timestamps that ignore injected input, and the `AttachThreadInput` restore come from NoFocusSteal at `279e9cd` (MIT), with its notice kept in `THIRD-PARTY-NOTICES.md`. The rules are new, because protecting a picked app and closing the 1.5 s window change them. This gives up a codebase with no third-party notices.
- **A switch counts only if you made it:** to the window you clicked, the one you Alt+Tabbed to, or one you opened from the taskbar or Start. There's no grace period after a keypress. NoFocusSteal lets any app take focus for 1.5 s after Enter, Tab, Esc or an F-key, and those keys are constant in coding and in games. This gives up a simpler rule: a link or file your click opens in another app needs its own case (M2).
- **Unelevated by default.** Windows hides input sent to an admin app from normal apps, and won't let a normal app take focus back from one. This gives up protecting admin apps until an elevated mode (Later).
- **No network code, so no in-app updater.** Updates come through winget and GitHub Releases. This gives up Gil Sweep's update-and-restart.
- **Never touch a game's process:** no injection, no memory reads and no input sent to it. Held keys aimed at a game are dropped, never replayed, because replayed keys are fake input. This keeps game terms of service and anti-cheat out of play, and gives up delivering keys typed into a game during a steal.
- **The brand is a placeholder:** a paw holding a window down, in `docs/brand/`. The real logo is an M0 item.

## M0: Placeholder (as soon as possible)

- [x] Add `LICENSE` (Apache-2.0), `README.md`, `ROADMAP.md` and `AGENTS.md`.
- [x] Add the placeholder mark to `docs/brand/`.
- [ ] Search trademarks for Staypaw: USPTO, and EUIPO if it will be sold in Europe. *(owner)*
- [ ] Pick the real logo in a claude.ai design project. Export `mark.svg` and `lockup.svg` with `-dark` variants, with the wordmark converted to paths. *(owner)*
- [ ] Scaffold `Staypaw.Core`, `Staypaw.Desktop` and `Staypaw.Tests` on .NET 10 and Avalonia 12, with central package versions and warnings as errors.
- [ ] A tray icon made from the mark, with Quit. No window yet.
- [ ] Design tokens: a spacing scale, a type scale, a palette with shades and a few shadow levels.
- [ ] NativeAOT publish for `win-x64` with no trim or AOT warnings.
- [ ] Guardrail tests, each with a positive control: `Staypaw.Core` doesn't reference Avalonia, and no Staypaw assembly uses `System.Net`.
- [ ] CI on Windows: build, test, and secret scanning with the gitleaks release binary, checked against its published checksum and run with `--log-opts="--full-history HEAD"`.

**Done when:** CI is green on `main`, and `dotnet publish -r win-x64` gives a native exe that starts in the tray.

## M1: Watch and log

- [ ] Port the watcher from NoFocusSteal `279e9cd` into `Staypaw.Core`, with its MIT notice in `THIRD-PARTY-NOTICES.md`.
- [ ] Log every focus change to `%LOCALAPPDATA%\Staypaw\focus-log.csv` with NoFocusSteal's columns, so logs from both tools can be compared.
- [ ] Port NoFocusSteal's steal test as a dev tool: a window that grabs focus on a timer and counts the keys it receives.
- [ ] Measure idle CPU and working set, and record them here.

**Done when:** a scripted steal shows up as a log row, the classification is covered by tests, and the idle numbers are recorded.

## M2: Protect

- [ ] Pick a protected app from the tray.
- [ ] Undo a focus change away from the protected app, or from the window you're typing in, unless you made the switch.
- [ ] Decide how a link or file your click opens in another app gets focus.
- [ ] Put focus back and flash the stealer's taskbar button.
- [ ] Show the last stealer and the top offender in the tray menu, and open the log from there.

**Done when:** a scripted steal against a protected window is undone, a switch to the window you clicked is allowed, and tests cover both, plus a steal right after a keypress.

## M3: Hold stray keys

- [ ] A `WH_KEYBOARD_LL` hook in Staypaw's own process that holds keys typed between a steal and the restore.
- [ ] Deliver held keys to your window after the restore, and drop them when that window is a protected game.
- [ ] Scan the published exe with Microsoft Defender, since a global keyboard hook can look like a keylogger.

**Done when:** the steal test counts 0 keys reaching the stealer, and a Defender scan of the published exe is clean.

## M4: 1.0

- [ ] A Velopack per-user Setup and a portable zip, built by a release workflow.
- [ ] Sign releases with Artifact Signing through OIDC, from a `release` environment that only `v*` tags can deploy to, as in Gil Sweep.
- [ ] Submit a winget manifest.
- [ ] A screenshot and a short demo in the README.

**Done when:** `winget install hazeliscoding.Staypaw` installs a signed build that starts in the tray.

## Later

- Let a window appear without taking your keyboard (the most-reacted ask in PowerToys #65).
- A pause hotkey and a timed pause.
- Rules by launcher: windows started by a coding agent or a Playwright server never get focus. Today's tools can only match by exe name, so an agent's Chromium and your own Chrome look the same.
- An elevated mode that can protect admin apps.

## Not planned

- **DLL injection or hooking other apps' APIs.** It breaks the trust contract, work laptops ban it, and anti-cheat flags it.
- **Anything that touches a game's process.** Game terms of service.
- **Reading controller input.** Protecting an app covers idle and controller play without it.
- **Network code:** an updater, telemetry or crash reporting.

## How we'll know it works

- The owner's dogfooding: coding and FFXIV sessions with the game protected log no steal that got through, and no key reaching a stealer.
- Public signals after release: issues, downloads and winget installs. No interviews or outreach.
