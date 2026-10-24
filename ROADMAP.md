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
- **The tray app loads Avalonia's Fluent theme** even with no window, because Windows' tray menu is drawn with Avalonia controls. Without it the menu opened empty and Quit couldn't be clicked. This gives up 6.5 MB of exe size (10.2 to 16.7 MB).
- **Three verdicts: Yours, Steal and Unclear.** Unclear is a switch right after a click in the window you were in, which may be a link or file you opened. The log counts those, so M2 can settle them with data. This gives up a simple two-way log.
- **Also yours:** an app whose process started after your last click or deliberate key and took focus within 10 s, a click on the taskbar, Start, search or a notification, and an Alt or Win key within 1.5 s. This gives up catching an app that steals right after you press Alt, which FFXIV's Alt hotbars make common (M2). *Superseded 2026-10-21 for Alt: only Alt+Tab and Alt+Esc count now (below).*
- **The watcher keeps NoFocusSteal's 15 ms foreground poll,** because some apps take focus without Windows announcing it. It's most of Staypaw's idle CPU (0.4% of one core).
- **M2 was built without log data,** at the owner's call. The two open questions were decided up front instead of from Unclear rows and game sessions. This gives up evidence for both answers.
- **A link or file you open is allowed:** an Unclear change goes through unless that app stole in the last 10 minutes, as in NoFocusSteal. This gives up catching a first-time stealer that lands right after your click.
- **Only Alt+Tab, Alt+Esc and the Win key are switch keys.** Alt with any other key, like FFXIV's Alt+number hotbars or Alt+F in menus, no longer lets a change through. The switch counts when Alt is released, as Windows does it. This gives up other Alt shortcuts that open a window from another app.
- **Staypaw restores focus after a steal from the protected app, or from any window you typed in during the last 1.5 s.** An idle steal from an unprotected app is only logged. This gives up NoFocusSteal's Strict mode, which blocks every unasked change everywhere.
- **One protected app, picked as "the app you were just in"** from the tray. Clicking the tray never changes that app, so the menu needs no window list. It's saved to `%APPDATA%\Staypaw\protected-app.txt`. This gives up protecting several apps at once.
- **One copy at a time,** through a named mutex. A second launch exits silently.

## Decisions (2026-10-23)

- **The wordmark is Fredoka SemiBold,** lowercase and outlined, beside the mark in `lockup.svg` and `lockup-dark.svg`, as in Sweepling. The README's title is the lockup. `scripts/make_lockup.py` builds both from the mark, so they follow it when the real logo arrives. This gives up a typeface of Staypaw's own: it shares Sweepling's.
- **Calla Keyes joins as Staypaw's character, beside the placeholder mark.** She is a calico homebody who sits beside your work like a cat on a warm keyboard and won't move unless you pick her up yourself. She appears in the README, in `docs/brand/calla/` and in launch posts. The paw mark stays the logo and the app icon. Her art was drawn with an AI image model, and it is Apache-2.0 like the code; the README says both. Her stay-put sticker stays out of the README until its see-through gaps are filled, because they show on GitHub's dark mode. This gives up a single mark, and the sticker that shows a steal being undone.
- **Calla greets you on first launch, and again from "About Staypaw" in the tray menu.** One small window shows her slow-blink sticker, what Staypaw does, how to protect an app (or which app is protected), the trust contract in a line and the version. A tray-only app otherwise starts with no sign at all. `%APPDATA%\Staypaw\welcomed` records that the welcome was shown. The window is built when it opens and dropped when it closes. This gives up 73 KB of exe (17,792,512 to 17,867,264 bytes), and adds a window at first launch: a focus change Staypaw makes itself, right after you launched it.
- **Calla costs no idle memory, but drawing anything costs about 21 MB that Staypaw keeps.** Each run launched the native build fresh on the same machine:

  | Build, state | Working set | Private |
  |---|---|---|
  | Before Calla, tray only, 60 s | 40.8 MB | 65.4 MB |
  | With Calla, tray only, 60 s | 40.9 MB | 64.4 MB |
  | With Calla, welcome open, 60 s | 66.3 MB | 86.0 MB |
  | With Calla, 120 s after closing it | 68.0 MB | 86.6 MB |
  | With Calla, tray menu open instead, 20 s | 62.6 MB | 81.5 MB |

  The tray menu costs as much as the window: the first thing Avalonia draws starts its renderer, which isn't released until Staypaw quits. The welcome pays at first launch what the first tray click pays anyway. This gives up M1's idle figure as the whole story: it holds only until the menu or the window first opens.

## Decisions (2026-10-24)

- **Held keys are replayed into your window, never into a game.** A window counts as a game when it's the protected app, an exe that Windows' Game Bar lists (read from the registry, so the game itself is never touched), or an app Staypaw can't name, since anti-cheat can hide even a process's name. Replayed keys are input the game didn't get from you. This gives up the keys typed in a steal's first milliseconds when the protected app isn't a game, and leaves a game that Windows doesn't list, and that you didn't protect, open to replayed keys.
- **Keys wait until Staypaw has judged a focus change and finished any restore.** A `WH_KEYBOARD_LL` hook on a thread of its own catches them. Every key on the system waits for that hook, and Windows quietly removes one that answers too slowly, so it can't share the UI thread, which may be busy drawing. Keys held for a change that stands go to the new window only if it was your switch; otherwise they're dropped, never sent to a stealer. This gives up keys typed in the first milliseconds of a change Staypaw lets stand, such as a link you opened.
- **A hold that makes no progress for 500 ms drops its keys and lets the keyboard go,** so a stuck watcher or a bug can't take the keyboard. This gives up those keys.
- **A restore calls `SetForegroundWindow` alone, without NoFocusSteal's `BringWindowToTop`.** Its extra activation could leave no window in front: in a steal test that typed without pause, about 40 of 1,000 keys reached no window, and each steal logged two `Restored` rows and counted twice toward the fight limit. `SetForegroundWindow` raises the window anyway. This gives up NoFocusSteal's exact restore.
- **A restore that leaves no window in front is finished from your window's thread.** After a steal, `SetForegroundWindow` can return true and still leave no window in front, for good: your keys go nowhere and the stealer can't even steal again. A plain retry never fixed it. While it lasts, for up to 500 ms, the 15 ms check retries with Staypaw's thread sharing the input of your window's thread (`AttachThreadInput`), which finished 17 of 17 such restores. It's the one time Staypaw shares a game's input state: for about a millisecond, with no process handle, no memory read and no input sent. A hung window is skipped, since it would hang Staypaw too. This gives up keeping Staypaw's input apart from a protected game's at all times.
- **Keys already in your window when a stealer takes focus can't be held.** A stealer that joins your window's input with `AttachThreadInput` and then takes focus gets the keys your window hasn't read yet, or they're lost when it lets go. The hook let them through rightly, because your window had focus: tagged keys put each one 0–3 ms before the steal. So the key test goes quiet for 30 ms before each steal it asks for, and checks the keys typed after it. This gives up keys typed in the last milliseconds before a steal, and a test of them.

## M0: Placeholder (as soon as possible)

- [x] Add `LICENSE` (Apache-2.0), `README.md`, `ROADMAP.md` and `AGENTS.md`.
- [x] Add the placeholder mark to `docs/brand/`.
- [ ] Search trademarks for Staypaw: USPTO, and EUIPO if it will be sold in Europe. *(owner)*
- [ ] Pick the real logo in a claude.ai design project, and export `mark.svg` with a `-dark` variant. `scripts/make_lockup.py` rebuilds the lockups from it. *(owner)*
- [x] Lockups of the placeholder mark with the wordmark, for light and dark backgrounds.
- [x] Scaffold `Staypaw.Core`, `Staypaw.Desktop` and `Staypaw.Tests` on .NET 10 and Avalonia 12, with central package versions and warnings as errors.
- [x] A tray icon made from the mark, with Quit. No window yet.
- [x] Design tokens: a spacing scale, a type scale, a palette with shades and a few shadow levels.
- [x] NativeAOT publish for `win-x64` with no trim or AOT warnings.
- [x] Guardrail tests, each with a positive control: `Staypaw.Core` doesn't reference Avalonia, and no Staypaw assembly uses `System.Net`. A deliberate `HttpClient` in Core failed the network test.
- [x] CI on Windows: build, test, and secret scanning with the gitleaks release binary, checked against its published checksum and run with `--log-opts="--full-history HEAD"`.

**Done when:** CI is green on `main`, and `dotnet publish -r win-x64` gives a native exe that starts in the tray.

## M1: Watch and log

- [x] Port the watcher from NoFocusSteal `279e9cd` into `Staypaw.Core`, with its MIT notice in `THIRD-PARTY-NOTICES.md`.
- [x] Log every focus change to `%LOCALAPPDATA%\Staypaw\focus-log.csv` with NoFocusSteal's columns, so logs from both tools can be compared.
- [x] Port NoFocusSteal's steal test as a dev tool: a window that grabs focus on a timer and counts the keys it receives.
- [x] Measure idle CPU and working set, and record them here. The native build, idle for 60 s on 2026-10-21: 234 ms of CPU (0.4% of one core), a 41 MB working set and 66 MB private.

**Done when:** a scripted steal shows up as a log row, the classification is covered by tests, and the idle numbers are recorded.

**Done 2026-10-21:** the native build logged the steal tool as `Steal` ("no click or switch asked for it"), and `StealTests` passes against the real desktop (`Staypaw.Tests.exe -explicit only`). `ClassifierTests` covers each rule.

## M2: Protect

- [x] Run one copy at a time, since two would fight over every restore.
- [x] Pick a protected app from the tray.
- [x] Undo a focus change away from the protected app, or from the window you're typing in, unless you made the switch.
- [x] Decide how a link or file your click opens in another app gets focus. Decided without log data (see Decisions).
- [x] Narrow the Alt and Win allowance, since FFXIV's hotbars use Alt with the number keys.
- [x] Put focus back and flash the stealer's taskbar button.
- [x] Show the last stealer and the top stealer in the tray menu, and open the log from there.

**Done when:** a scripted steal against a protected window is undone, a switch to the window you clicked is allowed, and tests cover both, plus a steal right after a keypress.

**Done 2026-10-21:** `StealTests` restored a scripted steal from the protected app on the real desktop: a `Restored` row, with the original window back in front. `ClassifierTests` covers the clicked switch, the steal right after a keypress, links and repeat stealers. `InputTrackerTests` covers the switch keys. In the native build, a second copy exited on its own, and Protect, Stop protecting and Quit worked from the tray menu.

## M3: Hold stray keys

- [x] A `WH_KEYBOARD_LL` hook in Staypaw's own process that holds keys typed between a steal and the restore.
- [x] Deliver held keys to your window after the restore, and drop them when that window is a protected game.
- [x] Scan the published exe with Microsoft Defender, since a global keyboard hook can look like a keylogger.

**Done when:** the steal test counts 0 keys reaching the stealer, and a Defender scan of the published exe is clean.

**Done 2026-10-24:** `StealTests` passed three runs in a row on the real desktop. In each, 45 keys typed during 15 steals were held and replayed, the stealer got none, and your window got all 120. With key holding turned off, the stealer got 36 and the test failed. A Defender custom scan of the published exe (signatures 1.461.184.0) found no threats. The exe is 17,896,448 bytes, 29 KB more than before M3. Idle in the tray, a build from before the last fixes used 40.8 MB working set and 65.1 MB private, against 40.9 and 64.4 before M3. `GuardrailTests` fails on any hook that isn't low-level; a deliberate `WH_KEYBOARD` failed it.

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
- Calla's stay-put sticker beside "Before and after", once its see-through gaps are filled in anime-pipeline.
- Calla's keys-held and on-guard stickers and her night-watch outfit, drawn in anime-pipeline. On-guard would be the README hero.
- Hand-drawn Calla art by [@wrenishacking](https://github.com/wrenishacking), on the way. Agree its license with the artist before it goes in, since the AI-drawn stickers are Apache-2.0 like the code.
- Try Avalonia's software renderer for the tray menu and the window: the default renderer keeps about 21 MB once either first opens.

## Not planned

- **DLL injection or hooking other apps' APIs.** It breaks the trust contract, work laptops ban it, and anti-cheat flags it.
- **Anything that touches a game's process.** Game terms of service.
- **Reading controller input.** Protecting an app covers idle and controller play without it.
- **Network code:** an updater, telemetry or crash reporting.

## How we'll know it works

- The owner's dogfooding: coding and FFXIV sessions with the game protected log no steal that got through, and no key reaching a stealer.
- Public signals after release: issues, downloads and winget installs. No interviews or outreach.
