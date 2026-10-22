# Project Flight

Windows dictation / second-brain app for consultants. **Open source (MIT)**, public repo `akhanrepo/flight-dictation`. Full product context lives in [docs/PRD.md](docs/PRD.md) (read its status note first — some original decisions were reversed), architecture in [docs/architecture.md](docs/architecture.md), and every feature's brief in [docs/briefs/](docs/briefs/). Read them before working on any feature.

## Tech stack (from PRD)

- **App shell/UI:** C# + WinUI 3 (Windows App SDK), .NET 8
- **Local STT:** Whisper.net 1.9.x (whisper.cpp bindings), model `ggml-base-q5_1.bin` — the **multilingual** base model, not the `.en` variant. Feature #3's five non-English source languages need it.
- **Local LLM:** **Decided: LLamaSharp 0.27.x (llama.cpp), CPU backend**, model Phi-4-mini-instruct Q4_K_M GGUF. Chosen for CPU-only mid-tier hardware. Engines sit behind `ILlmCompletion`; `ITextCleaner` is a consumer of it, not an engine. Run `Flight.App --benchmark` to measure real latency.
- **Audio capture:** NAudio 2.2.1 (3.x requires .NET 9), 16kHz mono
- **Cloud LLM:** **Decided: OpenAI `gpt-4o-mini` over the OpenAI-compatible `/chat/completions` shape, bring-your-own-key, no account.** Endpoint and model are `FlightOptions` settings, so moving to DeepSeek/Groq/Gemini-compat is config, not code. API key lives in `ISecretStore` (Windows DPAPI in the shell) — never in options or plaintext config.
- **Storage:** SQLite for the local vocabulary/context store. **sqlite-vec is not used**: it's a native loadable extension needing a per-RID binary, which would put a native download in the test path. Embeddings are float32 BLOBs ranked by cosine similarity inside `SqliteVocabularyRepository` — moving to a `vec0` virtual table later is a change inside that one class.
- **No backend.** The Supabase/Stripe subscription (old feature #5) was **removed** when the project went open source: a client-side paywall in an open-source app is trivially forked away, and cloud already runs on the user's key. It's in git history (before commit "Remove the subscription paywall") if ever needed. Don't reintroduce accounts or gating without a brief.
- **Config:** `src/Flight.App/appsettings.json` (`Flight` section) is bound onto `FlightOptions` by `AppHost`; `Flight__*` env vars override. It must never contain personal paths or secrets — it ships to everyone.
- **Packaging:** **Decided: Velopack** (unpackaged app; MSIX needs a trusted cert even to install). Pack id **`FlightApp`**, not `Flight`: Velopack installs to `%LOCALAPPDATA%\<packId>` and deletes it on uninstall, which would wipe the user's db and models. Update feed is `FlightOptions.UpdateFeed` = the public repo URL (GitHub Releases); a `vX.Y.Z` tag publishes a release via `release.yml`. For local installer testing, `scripts/pack-local.ps1 -Version X.Y.Z` builds into gitignored `artifacts\releases` (point an install at it with `Flight__UpdateFeed`). Self-contained publish (~187 MB, ~86 MB Setup.exe); models downloaded on first run. Unsigned until there's a certificate.

## MVP scope (build order)

1. Core dictation loop — hotkey, local STT, local LLM cleanup, preview toast, clipboard copy on confirm
2. Automatic vocabulary/context building from confirmed dictations — **built**
3. Bidirectional translation (up to 5 languages) — **built**
4. Cloud LLM toggle behind the same inference interface as #1 — **built**
5. ~~Account + subscription entitlement (Supabase + Stripe)~~ — built, then **removed** for the open-source release (#11)

Post-MVP features (briefs in `docs/briefs/`): 6 tray & app identity, 7 visual design, 8 configurable push-to-talk key, 9 mini indicator + automatic copy (replaced #1's preview), 10 speed, 11 open-source release — all **built**.

## Explicit non-goals (do not build, do not design for prematurely)

- Direct text injection into focused apps (clipboard copy only for MVP)
- Meeting capture (phase 2) and document generation (phase 3) — keep the VocabularyEntry/context schema generic enough to add them later, but build nothing for them now
- Agentic-coding integration as a distinct feature, team/multi-seat/SSO/audit logging, accounts or paywalls

## How we work (process rules)

- **Features are built one at a time from a brief the user provides.** Do not start a feature without a brief, and do not build ahead of the current brief.
- **Enter Plan Mode before implementing each feature** and get the plan approved before writing code.
- **Every acceptance criterion in a brief must map to at least one test.** If a criterion can't be tested, raise it before implementing.
- Commit to GitHub (`origin` = akhanrepo/upgraded-octo-flight) after each logical step or at end of session. Small, coherent commits.
- Update this file when architecture decisions land (e.g. the ONNX vs llama.cpp decision).

## Solution structure & file conventions

```
Flight.slnx
src/Flight.App/     WinUI 3 shell — thin UI layer only, no business logic
src/Flight.Core/    All logic, services, data access — where features live
tests/Flight.Tests/ xUnit tests, folder structure mirrors src/Flight.Core
docs/               PRD, architecture.md, briefs/featureN.md
```

- Logic goes in `Flight.Core` so it is testable without the WinUI runtime; `Flight.App` stays a thin shell wired through DI.
- All services are registered via `Flight.Core.Hosting.AppHost` (generic host). App and tests boot the same host.
- Tests mirror the source tree: `src/Flight.Core/Hosting/AppHost.cs` → `tests/Flight.Tests/Hosting/AppHostTests.cs`.
- Test naming: `<Subject>_<Behavior>` xUnit facts in `<Class>Tests.cs`.
- Nullable reference types enabled everywhere; keep them clean, no `!` suppressions without a comment.

## Testing philosophy

- `dotnet test Flight.slnx` must always pass on `main`; run it before every commit.
- Unit tests target `Flight.Core` directly — no WinUI test harness. UI stays thin enough that it doesn't need one.
- Prefer testing behavior through the DI container / public service interfaces over internals.
- STT/LLM inference are wrapped behind interfaces so tests use fakes; no tests that require downloaded models or network.

## Architecture (as built)

- `DictationOrchestrator` (Core) is the state machine: Idle → Capturing → Processing → Idle, with Error on failure or timeout (`FlightOptions.ProcessingTimeout`). There is **no preview/confirm** (feature #9): on success `DeliverAsync` writes the clipboard, then persists, then enqueues vocabulary — that order is an acceptance criterion, and **that is the only clipboard write in the app.** Keep it that way. `SpeechGate` (taps < 300 ms, silence) and `TranscriptFilter` (`[BLANK_AUDIO]` etc.) make sure non-speech never reaches the clipboard. Dispose (Quit) cancels in-flight processing without copying.
- Platform services live in `Flight.App` and are registered in `App.xaml.cs`: `LowLevelKeyboardHook` (`IKeyboardHook`), `WasapiAudioCapturer` (also raises `LevelChanged`), `WindowsClipboardWriter`, `DpapiSecretStore`, `VelopackAppUpdater`, and `DictationToastWindow`, which renders `ToastPresenter` (Core) on the UI thread.
- **Push-to-talk (feature #8):** configurable, hold-to-talk only, default **Right Alt**. `Hotkey` (Core) = canonical key set + text form + validation; stored in `settings.hotkey`. `PushToTalkHotkey` (Core) does all matching/swallowing/recording over `IKeyboardHook`, reading the hotkey live from settings. The shell hook only forwards events, reports `GetAsyncKeyState`, and sends the 0xE8 menu-mask key for Alt/Win hotkeys. A key-up is swallowed only if its key-down was.
- Data: `%LOCALAPPDATA%\Flight\flight.db` (SQLite `dictation_sessions`), models in `%LOCALAPPDATA%\Flight\models`, downloaded on first run.
- Test fakes for every pipeline interface are in `tests/Flight.Tests/Fakes/Fakes.cs`. Reuse them.
- **Vocabulary (feature #2):** `BackgroundVocabularyLearningQueue` drains a channel off the dictation loop and waits on `DictationActivity` while a dictation is capturing/processing. `PromptTextCleaner` injects the top-K relevant terms into the cleanup system prompt before inference; an empty store yields the unmodified prompt. Embeddings are the trigram `HashingEmbeddingGenerator` everywhere (feature #10 removed the LLM embedder — it was a second 3.8B forward pass per dictation); `VocabularyReembedder` migrates old-width vectors at startup.
- Extraction and cleanup both run through `ILlmCompletion`, so they follow whichever engine is active with no extra wiring.
- **LLM routing (feature #4):** `LlmModeRouter` is the registered `ILlmCompletion`; the local and cloud engines are wrapped in `LocalLlmEngine`/`CloudLlmEngine` so DI can tell three `ILlmCompletion`s apart. Routing comes from `LlmRequest.Mode` (set from the per-dictation settings snapshot), falling back to the current setting for background work. **There is no cloud→local fallback** — a cloud failure is an explicit error state, by requirement.
- **Languages (feature #3):** `SupportedLanguages.All` is the single source of truth for the six languages. Source language is user-selected, never auto-detected. `SqliteSettingsStore` caches the `settings` row in memory; the orchestrator snapshots `ISettingsStore.Current` at hotkey-release, so a change lands on the *next* dictation and can't affect one in flight. Cleanup and translation are one LLM pass (`PromptTextCleaner`), not two — a second round trip would double CPU inference latency.

- **Speed (feature #10):** `ModelWarmup` loads whisper (+ local LLM in local mode) in the background after startup. `LlamaCompletion` sizes each context to the request (≤ 4096, steps of 512). `PromptTextCleaner` skips the LLM for < 4 words or AI cleanup off (never when translating). Every dictation appends to `logs/timings.log` (`IDictationTimingsLog`). Measure, don't guess: there are no models in CI or the dev container.
- **Updates:** `Program.Main` (custom; `DISABLE_XAML_GENERATED_MAIN`) runs `VelopackApp.Build().Run()` first. `UpdateCheck` runs after startup, stages updates to apply on exit, never throws.
- **Tray (feature #6):** `TrayPresenter` (Core) owns every tray decision (status text naming the configured hotkey, icon state, menu items, commands); `Win32/NotifyIcon` (App) only renders `TrayState` via `Shell_NotifyIcon` and forwards clicks, on the UI thread. Shell-side actions go through `IShellActions` (implemented by `App`). Quit runs `ShutdownCoordinator`: cancel in-flight processing (clipboard untouched), unhook hotkey, close mic, bounded vocabulary drain (`ShutdownVocabularyGrace`). Pause is in-memory only. Start with Windows = HKCU Run value via `IStartupRegistration`, removed by Velopack's uninstall hook. `SingleInstanceGate` in `Program.Main` keeps one copy per user session; a second launch opens Settings in the first.
- **Indicator pill (features #7, #9):** `ToastPresenter` (Core) is the registered `IDictationUi`; it turns orchestrator calls into an immutable `ToastView` (tone: listening/processing/success/error/neutral, one-line title, icon-only or text, progress, `AutoHideAfter`) plus a separate high-rate `LevelChanged` for the mic bars. `DictationToastWindow` only renders it. Sizes are DIPs (`ToastLayout`: 120 compact, ≤ 360 with text, 36 high) converted per monitor; placement and the remembered dragged position are `ToastPlacement` + `JsonToastPositionStore`. **It never takes focus** (`WS_EX_NOACTIVATE`, shown without activation) — that's what lets the user paste immediately. `TranslucentBackdrop` = Desktop Acrylic pinned active with explicit per-theme tint/fallback colors (customising the controller stops it following the theme). **Never size windows in raw pixels** — that is what hid the old toast's text at 150%. Settings uses `WindowChrome` (Mica, custom title bar, DIP size, close-only) and the shared styles in `App.xaml` (`SettingsCardStyle`, `CaptionMutedTextStyle`).
- **Diagnostics:** unhandled/startup exceptions go to `%LOCALAPPDATA%\Flight\logs\errors.log` (`ErrorLog`). Check it first when the app runs with no window. `DisplayArea.FindAll()` must be indexed, not enumerated (WinAppSDK projection bug).
- **Brand:** `assets/brand/make_logo.py` generates the SVG master, PNGs and `flight*.ico` (normal/listening/paused) from one geometry; frames <= 32 px use a simplified mark. Regenerate rather than hand-editing. The App links the `.ico` files into `Assets\` and uses `flight.ico` as `ApplicationIcon`; `vpk pack --icon` uses it too.
- **CI:** `.github/workflows/ci.yml` (Windows: full test + app build; Linux: Core tests — keep Core and tests platform-neutral, e.g. no hard-coded `\` path separators), `codeql.yml` (C#, build-mode none), `release.yml` (tag `vX.Y.Z`; tests, packs, publishes a GitHub Release). Dependabot is configured in `.github/dependabot.yml`.

## Build/run notes (environment quirks)

- This machine has only the .NET 10 runtime; test project uses `RollForward=LatestMajor` to run net8.0 tests on it.
- `Flight.App` defaults to x64/`win-x64` when built via the solution (WinAppSDK self-contained rejects AnyCPU).
- Build: `dotnet build Flight.slnx` · Test: `dotnet test Flight.slnx`
- On non-Windows dev boxes (including remote/CI Linux containers) `Flight.App` cannot be fully built. Build/test Core only: `dotnet build src/Flight.Core/Flight.Core.csproj` · `dotnet test tests/Flight.Tests/Flight.Tests.csproj`

### How far `Flight.App` gets on Linux, and where it stops

Worth knowing so nobody re-walks these dead ends. With `-p:EnableWindowsTargeting=true` the restore and the Windows targeting packs both work; the build then dies in **`XamlCompiler.exe`**, a Windows-only PE binary. Running it under Mono (via `binfmt_misc`) gets it executing but it exits 1 silently — the XAML compiler resolves the WinUI type system out of `.winmd` Windows Metadata, which Mono has no support for. Wine wouldn't clear that either. The next blocker behind it is the MrtCore PRI resource indexer, same Windows-only problem.

**What can still be verified on Linux:** the hand-written code-behind type-checks if you skip the markup compiler and reference the WinUI assemblies directly. Build a scratch project (outside the repo) that targets `net8.0-windows10.0.19041.0` with `EnableWindowsTargeting=true`, does *not* set `UseWinUI` (that is what pulls in the markup compiler and PRI targets), `<Reference>`s the DLLs in `microsoft.windowsappsdk/<ver>/lib/net6.0-windows10.0.18362.0/` directly, `<Compile Include>`s `src/Flight.App/**/*.cs`, and supplies the markup compiler's output as generated stubs: one partial class per `.xaml` holding a field per `x:Name` (typed from the XAML element) plus an empty `InitializeComponent()`. Generating those fields *from the XAML* is what makes the check meaningful — a code-behind reference to a name the XAML doesn't define fails the compile exactly as it would on Windows.

**This does not substitute for a Windows build.** It verifies the C# and the XAML↔code-behind contract (names, handler signatures). It does not verify the XAML markup itself: bad property names, invalid `ThemeResource` keys, and malformed bindings all survive it.
- Local installer: `dotnet publish src/Flight.App/Flight.App.csproj -c Release -r win-x64 --self-contained -p:Platform=x64 -o publish` then `vpk pack --packId FlightApp --packVersion X.Y.Z --packDir publish --mainExe Flight.App.exe`.
