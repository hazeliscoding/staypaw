# AGENTS.md

These are the working rules for agents in this repo. Staypaw is a Windows tray app (.NET 10, Avalonia 12, NativeAOT, Apache-2.0) that stops other apps from taking your keyboard focus.

## Sources of truth

- `README.md`: the pitch and the trust contract.
- `ROADMAP.md`: decisions already made, what shipped, the milestones still to build, and what is out of scope. Check it before proposing features, and respect those decisions unless the owner reopens them. Work from the next unchecked item, tick it off when it's done, and don't build past the current milestone without asking. Record new decisions there, dated, in the same pull request as the change that needs them.
- New work goes in `ROADMAP.md` too: a box in a milestone, or a Later idea until it has one. GitHub issues are for bug reports and contributors' tasks, never roadmap work, and there are no milestones or Project boards. Don't add plan, spec or backlog documents.

## The trust contract (hard rules)

Staypaw watches the keyboard. People run it only if they can trust it, so never break these, not even in dev tooling that ships.

- **No code in other processes.** No DLL injection, no `CreateRemoteThread`, no hook DLLs loaded into other apps. Use only out-of-context WinEvent hooks, Raw Input and low-level hooks (`WH_KEYBOARD_LL`, `WH_MOUSE_LL`), which all run in Staypaw's own process. A test fails on any other hook type passed to `SetWindowsHookEx`. Work laptops ban injection, and anti-cheat flags it.
- **Never touch a game's process.** Identify a window's app with `PROCESS_QUERY_LIMITED_INFORMATION` and nothing more: no memory reads and no input sent to it. Held keys aimed at a game are dropped, never replayed. A game is the protected app, an exe that Windows' Game Bar lists, or an app Staypaw can't name, because anti-cheat can hide even a process's name. Game terms of service forbid tools that read or drive the client, and replayed keys are fake input. One exception, the owner's call: to finish a restore that left no window in front, Staypaw's thread shares the input of your window's thread for about a millisecond, even a game's (see `ROADMAP.md`).
- **The update check is the only network call.** Velopack makes it, from `Updates` in the tray app. No telemetry, no crash reports that Staypaw sends itself, and no other network use. Oopsling writes a crash report to disk, and only the user sends it: they file it from their own browser, or email it to the contact from their own mail app. Staypaw's own code and Oopsling never reference a networking assembly. `Staypaw.Core`, which sees your keys, never references Velopack either. Tests fail on both. A keyboard watcher that talks to the network for anything else can't be trusted.
- **Never record which key was pressed.** The log and memory keep only the time of a keypress. The one exception is held keys (M3): kept in memory until they're delivered or dropped, and never written anywhere. Never trace or log a `HeldKey`; it has no `ToString` beyond its type name for that reason. Anything more makes Staypaw a keylogger.
- **Run unelevated.** Don't set `requireAdministrator` in the manifest. An elevated mode is a Later item with its own decision.
- **Code ported from NoFocusSteal keeps its MIT notice** in `THIRD-PARTY-NOTICES.md`. The license requires it.

## Code

- .NET 10 with nullable on and warnings as errors. Package versions live in `Directory.Packages.props`.
- `Staypaw.Core` holds the watcher and the rules and never references Avalonia; a test enforces it. Rules take the event and the time as inputs and call no Win32, so tests run without real windows.
- Win32 calls go through CsWin32 (`NativeMethods.txt`), not hand-written `DllImport`. The generated code is typed and NativeAOT-safe.
- NativeAOT must stay free of warnings: no reflection-based serialization. Use source generators.
- The keyboard hook runs on a thread of its own, and its callback only compares and copies. Every key on the system waits for it, and Windows quietly removes a hook that answers too slowly.
- Velopack's package ID stays `hazeliscoding.Staypaw`, never `Staypaw`. Setup empties `%LOCALAPPDATA%\<packId>` on every install, and `%LOCALAPPDATA%\Staypaw` holds the log.
- Never restart into an update while the protected app is in front: nothing guards it during the restart. `UpdateGate` holds a downloaded update until you leave it.

## Brand

- The logo and the app icon are the paw mark. Calla, in `docs/brand/calla/`, is the character. Never edit, recolour or redraw her here: her stickers come from one written spec and one reference image, and a sticker changed by hand drifts from the rest of the set. Keep `stay-put.png` off dark backgrounds, GitHub's dark mode included, until its see-through gaps are filled, because they show as dark slivers.
- The wordmark is outlined Fredoka SemiBold. Use the lockup SVGs; don't re-typeset it with a web font. `scripts/make_lockup.py` rebuilds them from the mark, so rerun it when the mark changes.

## Dependencies

- **A special-purpose library** (a parser, an installer framework): add it yourself, with its reason in the commit message.
- **A general-purpose helper:** only after checking that nothing in the project already does the job.
- **A framework:** never without asking the owner.
- **Anything that brings in network code:** never, beyond Velopack for the update check. See the trust contract.

## Glossary

One meaning per term. Use these words in code, docs and commits.

- **Steal:** a focus change you didn't make.
- **Unclear:** a focus change right after a click in the window you were in. It may be a link or file you opened, or a steal.
- **Stealer:** the app whose window took focus.
- **Your switch:** a focus change to the window you clicked, Alt+Tabbed to, or opened from the taskbar or Start.
- **Protected app:** the app you picked. Nothing takes focus from it unless you switch away yourself.
- **Restore:** putting focus back on the window a steal took it from. The log's Result column says `Restored`.
- **Repeat stealer:** an app (exe plus window class) that stole in the last 10 minutes.
- **Held keys:** keys typed between a steal and its restore.

## Commands

- `dotnet build` builds everything. `dotnet test` runs the tests.
- `tests/Staypaw.Tests/bin/Release/net10.0-windows/Staypaw.Tests.exe -explicit only` runs the explicit tests, which steal focus on the real desktop, and one types an unused key (0x97) into its own test windows. Run them only when the owner isn't typing.
- `tools/Staypaw.StealTest` takes focus the way misbehaving apps do: `Staypaw.StealTest.exe <delaySeconds> [alt|attach|both] [repeatMs]`. It also steals whenever its thread gets `WM_APP`, and it prints how many keys reached it.
- `dotnet publish src/Staypaw.Desktop -c Release -r win-x64` builds the native exe. With Visual Studio 18, put `%ProgramFiles(x86)%\Microsoft Visual Studio\Installer` on `PATH` first: its `VsDevCmd.bat` calls `vswhere.exe` without a path, and the error text breaks the NativeAOT link step ("'vswhere.exe' is not recognized").
- `dotnet run --project tools/Staypaw.Demo -- artifacts/publish/Staypaw.exe docs/screenshots/demo.gif` records the README's demo with ffmpeg on the real desktop: the same typing and steals, without Staypaw and then with it. It types for about 20 seconds, so run it only when the owner isn't typing, with Staypaw quit. Its window is the protected app, whose held keys are dropped as a game's are, so its typing waits out each restore.
- `dotnet run --project tools/Staypaw.Screenshots -- docs/screenshots` renders the README's welcome and tray menu pictures headlessly, from made-up state, in light and dark. Rerun it when either changes.
- `.github/workflows/release.yml` builds Setup and the portable zip with `dotnet vpk` (a local tool: `dotnet tool restore`). A `v*` tag makes a draft release; a run by hand builds the files alone.
- `.github/workflows/winget.yml` sends each published stable release to winget with Komac, once the `WINGET_TOKEN` secret exists.
- To try an update locally: publish twice, and `dotnet vpk pack` two versions into one scratch folder with `release.yml`'s arguments but a test `--packId`, such as `hazeliscoding.Staypaw.UpdateTest`, so an installed Staypaw is never touched. Keep the first version's Setup from before the second pack. Quit Staypaw, since only one runs at a time. Install the test Setup with `--silent`, then start its `current\Staypaw.exe` with `STAYPAW_UPDATE_SOURCE` set to that folder: about a minute later it updates itself. Its `Update.exe --uninstall --silent` removes it.
- `dotnet run scripts/make-icons.cs` regenerates `docs/brand/staypaw.ico` and `app-icon-256.png` from `app-icon.svg`.
- `uv run --no-project --with fonttools --with uharfbuzz python -I scripts/make_lockup.py <Fredoka-SemiBold.ttf> docs/brand` rebuilds the lockups. Its header says where to get the font.

## Working style

- **Commits:** [Conventional Commits](https://www.conventionalcommits.org/) (`feat:`, `fix:`, `docs:`, `chore:`, `test:`, `ci:`, `build:`, `refactor:`). Keep each commit atomic, and use a scope when it adds clarity (`feat(rules): …`).
- **No AI attribution** in commits or PRs. That means no `Co-Authored-By` trailers, no "Generated with" lines and no session links.
- **Pull requests:** `main` is protected, so nothing is pushed to it directly. Branch from `main`, named like the commit types (`feat/…`, `fix/…`, `docs/…`), push, and open a pull request with `gh pr create`. It merges once CI's `build` check passes on a branch that's up to date with `main`, by rebase (`gh pr merge --rebase --delete-branch`), so each atomic commit lands as it is. Never bypass the check or force-push `main`.
- Renaming the `build` job in `ci.yml` means updating the required check in the `main` ruleset in the same change, or every pull request waits for a check that never comes.
- **Releases:** the version bump goes through a pull request. Once it's merged, tag that commit on `main` and push the tag. `v*` tags can't be moved or deleted.
- **Checks:** automate acceptance checks instead of handing manual steps to the owner. Give every check that tests for an absence a positive control. For example, the network guard must fail on a deliberate `HttpClient`.
- **Structural rules get tests.** When a decision is a rule about the code's shape, add a test that fails when it's broken.
- **Validation:** evidence comes from dogfooding and public signals (issues, downloads). Don't plan interviews, recruiting or outreach.
- **Docs:** short and concise.
- **Code comments:** explain why, not what. Comment only what the code can't say for itself: a non-obvious constraint, a Win32 quirk and its cause, or a line that keeps the trust contract. Don't restate names or types, and don't leave commented-out code.
- Local Playwright output goes to `.playwright-mcp/`, which git ignores.
