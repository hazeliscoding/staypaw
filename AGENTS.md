# AGENTS.md

These are the working rules for agents in this repo. Staypaw is a Windows tray app (.NET 10, Avalonia 12, NativeAOT, Apache-2.0) that stops other apps from taking your keyboard focus.

## Sources of truth

- `README.md`: the pitch and the trust contract.
- `ROADMAP.md`: decisions already made, the milestones, and what is out of scope. Check it before proposing features. Respect those decisions unless the owner reopens them.
- Work from the next unchecked item in `ROADMAP.md`. Don't build past the current milestone without asking.

## The trust contract (hard rules)

Staypaw watches the keyboard. People run it only if they can trust it, so never break these, not even in dev tooling that ships.

- **No code in other processes.** No DLL injection, no `CreateRemoteThread`, no hook DLLs loaded into other apps. Use only out-of-context WinEvent hooks, Raw Input and low-level hooks (`WH_KEYBOARD_LL`, `WH_MOUSE_LL`), which all run in Staypaw's own process. A test fails on any other hook type passed to `SetWindowsHookEx`. Work laptops ban injection, and anti-cheat flags it.
- **Never touch a game's process.** Identify a window's app with `PROCESS_QUERY_LIMITED_INFORMATION` and nothing more: no memory reads and no input sent to it. Held keys aimed at a game are dropped, never replayed. A game is the protected app, an exe that Windows' Game Bar lists, or an app Staypaw can't name, because anti-cheat can hide even a process's name. Game terms of service forbid tools that read or drive the client, and replayed keys are fake input. One exception, the owner's call: to finish a restore that left no window in front, Staypaw's thread shares the input of your window's thread for about a millisecond, even a game's (see `ROADMAP.md`).
- **No networking code.** No `HttpClient`, sockets, updater or telemetry, and no package that brings them in. A test fails on any `System.Net` use in Staypaw's assemblies. A keyboard watcher that talks to the network can't be trusted.
- **Never record which key was pressed.** The log and memory keep only the time of a keypress. The one exception is held keys (M3): kept in memory until they're delivered or dropped, and never written anywhere. Never trace or log a `HeldKey`; it has no `ToString` beyond its type name for that reason. Anything more makes Staypaw a keylogger.
- **Run unelevated.** Don't set `requireAdministrator` in the manifest. An elevated mode is a Later item with its own decision.
- **Code ported from NoFocusSteal keeps its MIT notice** in `THIRD-PARTY-NOTICES.md`. The license requires it.

## Code

- .NET 10 with nullable on and warnings as errors. Package versions live in `Directory.Packages.props`.
- `Staypaw.Core` holds the watcher and the rules and never references Avalonia; a test enforces it. Rules take the event and the time as inputs and call no Win32, so tests run without real windows.
- Win32 calls go through CsWin32 (`NativeMethods.txt`), not hand-written `DllImport`. The generated code is typed and NativeAOT-safe.
- NativeAOT must stay free of warnings: no reflection-based serialization. Use source generators.
- The keyboard hook runs on a thread of its own, and its callback only compares and copies. Every key on the system waits for it, and Windows quietly removes a hook that answers too slowly.

## Brand

- The logo and the app icon are the paw mark. Calla, in `docs/brand/calla/`, is the character. Never edit, recolour or redraw her here: her stickers come from one written spec and one reference image, and a sticker changed by hand drifts from the rest of the set. Keep `stay-put.png` off dark backgrounds, GitHub's dark mode included, until its see-through gaps are filled, because they show as dark slivers.
- The wordmark is outlined Fredoka SemiBold. Use the lockup SVGs; don't re-typeset it with a web font. `scripts/make_lockup.py` rebuilds them from the mark, so rerun it when the mark changes.

## Dependencies

- **A special-purpose library** (a parser, an installer framework): add it yourself, with its reason in the commit message.
- **A general-purpose helper:** only after checking that nothing in the project already does the job.
- **A framework:** never without asking the owner.
- **Anything that brings in network code:** never. See the trust contract.

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
- `dotnet run scripts/make-icons.cs` regenerates `docs/brand/staypaw.ico` and `app-icon-256.png` from `app-icon.svg`.
- `uv run --no-project --with fonttools --with uharfbuzz python -I scripts/make_lockup.py <Fredoka-SemiBold.ttf> docs/brand` rebuilds the lockups. Its header says where to get the font.

## Working style

- **Commits:** [Conventional Commits](https://www.conventionalcommits.org/) (`feat:`, `fix:`, `docs:`, `chore:`, `test:`, `ci:`, `build:`, `refactor:`). Keep each commit atomic, and use a scope when it adds clarity (`feat(rules): …`).
- **No AI attribution** in commits or PRs. That means no `Co-Authored-By` trailers, no "Generated with" lines and no session links.
- **Checks:** automate acceptance checks instead of handing manual steps to the owner. Give every check that tests for an absence a positive control. For example, the network guard must fail on a deliberate `HttpClient`.
- **Structural rules get tests.** When a decision is a rule about the code's shape, add a test that fails when it's broken.
- **Validation:** evidence comes from dogfooding and public signals (issues, downloads). Don't plan interviews, recruiting or outreach.
- **Docs:** short and concise. Prefer editing `ROADMAP.md` over creating new planning documents.
- **Code comments:** explain why, not what. Comment only what the code can't say for itself: a non-obvious constraint, a Win32 quirk and its cause, or a line that keeps the trust contract. Don't restate names or types, and don't leave commented-out code.
- Local Playwright output goes to `.playwright-mcp/`, which git ignores.
