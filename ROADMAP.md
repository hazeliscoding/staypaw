# Roadmap

Staypaw is a Windows tray app that stops other apps from taking your keyboard focus. This file holds the decisions already made, what shipped and what's out of scope. Open work is in GitHub issues; **Next** says where.

## Architecture characteristics

What the design must protect. Everything else gets ordinary good design.

1. **Speed.** Focus comes back before you notice, and no key you type during a steal reaches the stealer.
2. **Trust.** Staypaw watches the keyboard, so nothing is injected, nothing goes over the network but the update check, no key is stored, and builds are signed.
3. **Footprint.** It idles all day beside a game, so it uses close to no CPU and little memory while nothing happens.

## Decisions (2026-10-21)

- **Build Staypaw** instead of contributing to NoFocusSteal. Protecting a picked app, holding keys and signed winget installs change its design, and it has one maintainer who has been quiet since 2026-09-28. This gives up a head start inside an existing project, and the longer log run that was meant to come first: the first run caught no game data.
- **Speed, trust and footprint** are the characteristics above. Compatibility with admin apps and exclusive-fullscreen games comes second.
- **Public, under Apache-2.0.** A keyboard watcher is easier to trust when people can read it. This gives up a closed-source paid app; the paid layer is still open.
- **.NET 10, Avalonia 12 and NativeAOT,** laid out like Gil Sweep: `Staypaw.Core` (the watcher and the rules, no UI), `Staypaw.Desktop` (the tray app) and `Staypaw.Tests`. Win32 calls go through CsWin32. This gives up NoFocusSteal's ~100 KB exe on .NET Framework 4.8; a NativeAOT exe needs no runtime either, but it's bigger.
- **Port NoFocusSteal's watcher, write our own rules.** The WinEvent hooks, the 15 ms foreground poll, the Raw Input timestamps that ignore injected input, and the `AttachThreadInput` restore come from NoFocusSteal at `279e9cd` (MIT), with its notice kept in `THIRD-PARTY-NOTICES.md`. The rules are new, because protecting a picked app and closing the 1.5 s window change them. This gives up a codebase with no third-party notices.
- **A switch counts only if you made it:** to the window you clicked, the one you Alt+Tabbed to, or one you opened from the taskbar or Start. There's no grace period after a keypress. NoFocusSteal lets any app take focus for 1.5 s after Enter, Tab, Esc or an F-key, and those keys are constant in coding and in games. This gives up a simpler rule: a link or file your click opens in another app needs its own case (M2).
- **Unelevated by default.** Windows hides input sent to an admin app from normal apps, and won't let a normal app take focus back from one. This gives up protecting admin apps until an elevated mode (Later).
- **No network code, so no in-app updater.** Updates come through winget and GitHub Releases. This gives up Gil Sweep's update-and-restart. *Superseded 2026-10-25: Staypaw updates itself, as Sweepling does (below).*
- **Never touch a game's process:** no injection, no memory reads and no input sent to it. Held keys aimed at a game are dropped, never replayed, because replayed keys are fake input. This keeps game terms of service and anti-cheat out of play, and gives up delivering keys typed into a game during a steal.
- **The brand is a placeholder:** a paw holding a window down, in `docs/brand/`. The real logo is an M0 item. *Superseded 2026-10-24: the paw is the logo (below).*
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
- **Setup is Velopack's, without its SDK.** The SDK brings in network code, so Staypaw answers Velopack's hooks itself: it exits at once when Setup or the uninstaller runs it with `--veloapp-install` or the like, which they'd otherwise wait 30 s for and then call a failed install. Setup, and the `Update.exe` that uninstalls Staypaw, still carry Velopack's code for downloading missing runtimes. Staypaw needs none, so they download nothing, and Staypaw never runs them. Releases have no update packages or feed. This gives up Velopack's updates, and an install with no network code anywhere; the portable zip has none. *Superseded 2026-10-25: Staypaw uses Velopack's SDK for its updates, and the SDK answers the hooks.*
- **Velopack's package ID is `hazeliscoding.Staypaw`,** so Setup installs to `%LOCALAPPDATA%\hazeliscoding.Staypaw`. Setup empties that folder on every install and the uninstaller deletes it, and `%LOCALAPPDATA%\Staypaw` holds the log, so an ID of `Staypaw` would wipe the log at every upgrade. The ID matches winget's and the mutex's. The log and the settings stay after an uninstall. This gives up an install folder named plainly Staypaw.
- **Releases are signed in a job of their own.** The build job runs the tests and the build, and every package they load, so it can't get a token. The package job takes the built files and only packs and signs them with vpk. A compromised package could otherwise take the token and sign files of its own with the owner's certificate, unseen in any release. A package that changes the build is still signed with it. This gives up a single job: a release starts a second Windows runner and passes the build between them.
- **The paw is the logo,** the owner's call. The mark that was a placeholder, a paw holding a window down, stays the logo and the app icon. This gives up a logo picked in a design project.
- **No trademark search,** the owner's call. This gives up knowing before 1.0 whether someone else holds the name.
- **Staypaw starts with Windows,** from the current user's Run key, and winget runs Setup with `--silent` both ways, as in Prompuff. A silent Setup doesn't start Staypaw, and an upgrade stops it without starting it again; without a Run entry, protection stayed off after every upgrade and every restart until you started Staypaw yourself. First launch turns it on, the welcome says so, and the tray menu turns it off. The uninstall hook removes the entry only if it starts this copy, so a portable copy keeps its own. A Defender scan of the exe with the entry and the keyboard hook found no threats (signatures 1.461.184.0). This gives up a `winget install` that starts Staypaw: you start it once from Start, and after an upgrade it's back at the next sign-in. Running Setup without `--silent` would start it, but on an upgrade it waits for a click on Velopack's Update dialog.

## Decisions (2026-10-25)

- **Later versions reach winget through Komac,** from a workflow that runs when a stable release is published, as in Prompuff. Staypaw never updates itself, so winget is how its users get new versions. 1.0.0 went in by hand, since Komac only updates a package winget already has. This gives up a narrow token: Komac needs a classic one with `public_repo`, which can write to all the owner's public repos. *From 1.1, Staypaw also updates itself; winget still serves new installs, 1.0 and copies with updates off.*
- **Staypaw updates itself, as Sweepling does,** the owner's call, reopening "no in-app updater". Velopack checks GitHub Releases a minute after start and every 4 hours, downloads in the background and restarts into the update. "Update automatically" in the tray menu turns it off. Only a copy installed with Setup checks: the portable zip and a build from source never do. The check is the only network call. Staypaw's own code still references no networking assembly, and `Staypaw.Core`, which sees your keys, doesn't reference Velopack; tests fail on either. This gives up "nothing goes over the network", the trust contract's plainest promise, and puts Velopack's HTTP code in the process that holds the keyboard hook. It also costs footprint: the exe grew from 17.9 to 24.0 MB, and an installed copy that has checked GitHub idles at 49.6 MB working set and 17.1 MB private with 15 threads, against 40.1 MB, 13.9 MB and 7 for 1.0.
- **A restart into an update waits while the protected app is in front,** because nothing guards it for the seconds a restart takes. The update installs at the first focus change away from it, or at the next start. This gives up updating at once during a long game.
- **Open work moves to GitHub issues and milestones,** the owner's choice, as sdl3-porter did after 1.0. With two releases out, open work is easier to find, follow and comment on as issues, and the [Staypaw board](https://github.com/users/hazeliscoding/projects/12) shows all of it. M5's last item became the v1.1.1 milestone (#2). A Getting found milestone, added on the way, holds the winget install with M4's open "Done when" (#3) and a SmartScreen check (#4). Later became #5 to #12, labelled `later`. The old checklists are in `git show v1.1.0:ROADMAP.md`. This gives up changing the plan in the same pull request as the code, and agents now read it through `gh`.
- **`main` takes changes only by pull request,** the owner's call, as in Gil Sweep and Prompuff. A ruleset with no bypass refuses direct pushes, force-pushes and deleting `main`, requires CI's `build` check on a branch that's up to date with it, and merges by rebase only, so every commit on `main` has built and tested. This gives up pushing straight to `main`, which every change before this one did.
- **The 1.x releases go smallest first,** the owner's call: v1.2 pause (#6), v1.3 rules by launcher (#7), v1.4 a window that appears without taking your keyboard (#5). Each release ships something soon, and the hardest design jobs get the most time. The software renderer (#12) joins v1.1.1, so the first release an installed copy fetches by itself carries a real change. Each milestone's open questions are settled as a dated decision before it's built. The elevated mode (#8) stays `later` until the log shows how often admin apps steal, and Calla's art (#9, #10, #11) stays `later` until it's drawn. This gives up the most-reacted ask in PowerToys #65 first: it comes last.

## Shipped

| Version | Released | What it brought |
|---|---|---|
| [1.0.0](https://github.com/hazeliscoding/staypaw/releases/tag/v1.0.0) | 2026-10-25 | M0 to M4: a log of every steal, a protected app, held keys, a signed Setup and portable zip, and Start with Windows. |
| [1.1.0](https://github.com/hazeliscoding/staypaw/releases/tag/v1.1.0) | 2026-10-25 | Updates itself from GitHub Releases, never while the protected app is in front. |

## Next

The [Staypaw board](https://github.com/users/hazeliscoding/projects/12) shows all open work. The current milestone is [v1.1.1](https://github.com/hazeliscoding/staypaw/milestone/1).

| Milestone | What it brings |
|---|---|
| [v1.1.1](https://github.com/hazeliscoding/staypaw/milestone/1) | Proves that 1.1 updates itself: an installed 1.1.0 updates itself to 1.1.1 from GitHub. Tries the software renderer. |
| [v1.2](https://github.com/hazeliscoding/staypaw/milestone/3) | Pause: stop Staypaw for a while without quitting it, from a hotkey or for a set time. |
| [v1.3](https://github.com/hazeliscoding/staypaw/milestone/4) | Agents: windows that coding agents and Playwright servers start never take your keyboard. |
| [v1.4](https://github.com/hazeliscoding/staypaw/milestone/5) | Show, don't take: a window can appear without taking your keyboard. |
| [Getting found](https://github.com/hazeliscoding/staypaw/milestone/2) | Staypaw where people look for it, starting with winget. It needs no release. |

Ideas with no milestone yet are labelled [`later`](https://github.com/hazeliscoding/staypaw/issues?q=is%3Aopen+label%3Alater).

## Not planned

- **DLL injection or hooking other apps' APIs.** It breaks the trust contract, work laptops ban it, and anti-cheat flags it.
- **Anything that touches a game's process.** Game terms of service.
- **Reading controller input.** Protecting an app covers idle and controller play without it.
- **Network code beyond the update check:** telemetry or crash reporting.

## How we'll know it works

- The owner's dogfooding: coding and FFXIV sessions with the game protected log no steal that got through, and no key reaching a stealer.
- Public signals after release: issues, downloads and winget installs. No interviews or outreach.
