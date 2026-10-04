# Project Flight

Windows dictation / second-brain app for consultants. Full product context lives in [docs/PRD.md](docs/PRD.md) — read it before working on any feature.

## Tech stack (from PRD)

- **App shell/UI:** C# + WinUI 3 (Windows App SDK), .NET 8
- **Local STT:** Whisper.net 1.9.x (whisper.cpp bindings), model `ggml-base-q5_1.bin` — the **multilingual** base model, not the `.en` variant. Feature #3's five non-English source languages need it.
- **Local LLM:** **Decided: LLamaSharp 0.27.x (llama.cpp), CPU backend**, model Phi-4-mini-instruct Q4_K_M GGUF. Chosen for CPU-only mid-tier hardware. Engines sit behind `ILlmCompletion`; `ITextCleaner` is a consumer of it, not an engine. Run `Flight.App --benchmark` to measure real latency.
- **Audio capture:** NAudio 2.2.1 (3.x requires .NET 9), 16kHz mono
- **Cloud LLM:** **Decided: OpenAI `gpt-4o-mini` over the OpenAI-compatible `/chat/completions` shape.** Endpoint and model are `FlightOptions` settings, so moving to DeepSeek/Groq/Gemini-compat is config, not code. API key lives in `ISecretStore` (Windows DPAPI in the shell) — never in options or plaintext config.
- **Storage:** SQLite for the local vocabulary/context store. **sqlite-vec is not used**: it's a native loadable extension needing a per-RID binary, which would put a native download in the test path. Embeddings are float32 BLOBs ranked by cosine similarity inside `SqliteVocabularyRepository` — moving to a `vec0` virtual table later is a change inside that one class.
- **Backend:** Supabase project `kcemhjdmvunqcnoqwpyr`, code in `supabase/`: `subscriptions` table (RLS read-own, service-role writes) + Edge Functions `entitlement`, `stripe-checkout`, `stripe-portal`, `stripe-webhook`. Gateway `verify_jwt` is off; each function verifies the caller via `auth.getUser` (webhook via Stripe signature). The Stripe secret key lives only in function secrets. The client gets `SupabaseUrl`/`SupabaseAnonKey` (publishable, public) from `src/Flight.App/appsettings.json`, bound onto `FlightOptions` by `AppHost` (`Flight` section; `Flight__*` env vars override).
- **Function secrets** (set with `npx supabase secrets set`, never committed): `STRIPE_SECRET_KEY`, `STRIPE_PRICE_ID`, `STRIPE_WEBHOOK_SECRET`, optional `FLIGHT_RETURN_URL`.
- **Packaging:** **Decided: Velopack** (unpackaged app; MSIX needs a trusted cert even to install). Pack id **`FlightApp`**, not `Flight`: Velopack installs to `%LOCALAPPDATA%\<packId>` and deletes it on uninstall, which would wipe the user's db and models. Updates come from the public `akhanrepo/flight-releases` repo (`FlightOptions.UpdateFeedUrl`) because the source repo is private. Self-contained publish (~187 MB, ~86 MB Setup.exe); models downloaded on first run. Unsigned until there's a certificate.

## MVP scope (build order)

1. Core dictation loop — hotkey, local STT, local LLM cleanup, preview toast, clipboard copy on confirm
2. Automatic vocabulary/context building from confirmed dictations — **built**
3. Bidirectional translation (up to 5 languages) — **built**
4. Cloud LLM toggle behind the same inference interface as #1 — **built**
5. Account + subscription entitlement (Supabase + Stripe) — **built**

## Explicit non-goals (do not build, do not design for prematurely)

- Direct text injection into focused apps (clipboard copy only for MVP)
- Meeting capture (phase 2) and document generation (phase 3) — keep the VocabularyEntry/context schema generic enough to add them later, but build nothing for them now
- Open source release, agentic-coding integration as a distinct feature, team/multi-seat/SSO/audit logging

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
docs/               PRD and feature briefs
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

- `DictationOrchestrator` (Core) is the state machine: Idle → Capturing → Processing → Preview → Idle, with Error on failure or timeout (`FlightOptions.ProcessingTimeout`). **The clipboard is written only in `ConfirmAsync`.** Keep it that way.
- Platform services live in `Flight.App` and are registered in `App.xaml.cs`: `LowLevelKeyboardHook` (push-to-talk), `WasapiAudioCapturer`, `WindowsClipboardWriter`, and `DictationToastWindow` with `DictationUiPresenter`, which marshals calls to the UI thread.
- Push-to-talk key: **Right Alt** (`VK_RMENU`), fixed. There's no remapping UI yet.
- Data: `%LOCALAPPDATA%\Flight\flight.db` (SQLite `dictation_sessions`), models in `%LOCALAPPDATA%\Flight\models`, downloaded on first run.
- Test fakes for every pipeline interface are in `tests/Flight.Tests/Fakes/Fakes.cs`. Reuse them.
- **Vocabulary (feature #2):** `BackgroundVocabularyLearningQueue` drains a channel off the dictation loop. `ConfirmAsync` writes the clipboard, persists, *then* enqueues — that order is an acceptance criterion, don't reorder it. `PromptTextCleaner` injects the top-K relevant terms into the cleanup system prompt before inference; an empty store yields the unmodified prompt.
- Extraction and cleanup both run through `ILlmCompletion`, so they follow whichever engine is active with no extra wiring.
- **Entitlement (feature #5):** decided policy — trial **30 days** from the first cloud dictation; entitlement cache refreshed when older than **24h**; **7-day** offline grace; past that, or with no cache, cloud **fails closed** (local is the free tier and needs no verification, so the usual fail-open instinct is wrong here). Trial start is `MIN(created_at) WHERE llm_mode='cloud'`, so pre-existing cloud history back-dates the trial rather than resetting it on reinstall.
- `EntitlementGatedLlmCompletion` wraps the **cloud engine only**, inside `CloudLlmEngine`. Two guarantees are structural because of that placement: local dictation never runs an entitlement check, and a block always precedes the API call. Don't move it up to the router.
- **LLM routing (feature #4):** `LlmModeRouter` is the registered `ILlmCompletion`; the local and cloud engines are wrapped in `LocalLlmEngine`/`CloudLlmEngine` so DI can tell three `ILlmCompletion`s apart. Routing comes from `LlmRequest.Mode` (set from the per-dictation settings snapshot), falling back to the current setting for background work. **There is no cloud→local fallback** — a cloud failure is an explicit error state, by requirement.
- **Languages (feature #3):** `SupportedLanguages.All` is the single source of truth for the six languages. Source language is user-selected, never auto-detected. `SqliteSettingsStore` caches the `settings` row in memory; the orchestrator snapshots `ISettingsStore.Current` at hotkey-release, so a change lands on the *next* dictation and can't affect one in flight. Cleanup and translation are one LLM pass (`PromptTextCleaner`), not two — a second round trip would double CPU inference latency.

- **Sessions:** Supabase access tokens expire hourly. All backend calls go through `IAccountSession.CallAsync`, which refreshes once on 401; a rejected refresh signs out and throws `SessionExpiredException` (reported as "sign in again", not offline). Signup with no session (Confirm email on) throws `EmailConfirmationRequiredException`.
- **Updates:** `Program.Main` (custom; `DISABLE_XAML_GENERATED_MAIN`) runs `VelopackApp.Build().Run()` first. `UpdateCheck` runs after startup, stages updates to apply on exit, never throws.
- **CI:** `.github/workflows/ci.yml` (dotnet test + app build; deno test/check), `backend-deploy.yml` (needs `SUPABASE_ACCESS_TOKEN`, `SUPABASE_DB_PASSWORD` secrets), `release.yml` (tag `vX.Y.Z`; needs `RELEASES_TOKEN` to publish).

## Build/run notes (environment quirks)

- This machine has only the .NET 10 runtime; test project uses `RollForward=LatestMajor` to run net8.0 tests on it.
- `Flight.App` defaults to x64/`win-x64` when built via the solution (WinAppSDK self-contained rejects AnyCPU).
- Build: `dotnet build Flight.slnx` · Test: `dotnet test Flight.slnx`
- On non-Windows dev boxes (including remote/CI Linux containers) `Flight.App` cannot be fully built. Build/test Core only: `dotnet build src/Flight.Core/Flight.Core.csproj` · `dotnet test tests/Flight.Tests/Flight.Tests.csproj`

### How far `Flight.App` gets on Linux, and where it stops

Worth knowing so nobody re-walks these dead ends. With `-p:EnableWindowsTargeting=true` the restore and the Windows targeting packs both work; the build then dies in **`XamlCompiler.exe`**, a Windows-only PE binary. Running it under Mono (via `binfmt_misc`) gets it executing but it exits 1 silently — the XAML compiler resolves the WinUI type system out of `.winmd` Windows Metadata, which Mono has no support for. Wine wouldn't clear that either. The next blocker behind it is the MrtCore PRI resource indexer, same Windows-only problem.

**What can still be verified on Linux:** the hand-written code-behind type-checks if you skip the markup compiler and reference the WinUI assemblies directly. Build a scratch project (outside the repo) that targets `net8.0-windows10.0.19041.0` with `EnableWindowsTargeting=true`, does *not* set `UseWinUI` (that is what pulls in the markup compiler and PRI targets), `<Reference>`s the DLLs in `microsoft.windowsappsdk/<ver>/lib/net6.0-windows10.0.18362.0/` directly, `<Compile Include>`s `src/Flight.App/**/*.cs`, and supplies the markup compiler's output as generated stubs: one partial class per `.xaml` holding a field per `x:Name` (typed from the XAML element) plus an empty `InitializeComponent()`. Generating those fields *from the XAML* is what makes the check meaningful — a code-behind reference to a name the XAML doesn't define fails the compile exactly as it would on Windows.

**This does not substitute for a Windows build.** It verifies the C# and the XAML↔code-behind contract (names, handler signatures). It does not verify the XAML markup itself: bad property names, invalid `ThemeResource` keys, and malformed bindings all survive it.
- Backend tests: `deno test supabase/functions/` (Deno via `npm i -g deno`). Local installer: `dotnet publish src/Flight.App/Flight.App.csproj -c Release -r win-x64 --self-contained -p:Platform=x64 -o publish` then `vpk pack --packId FlightApp --packVersion X.Y.Z --packDir publish --mainExe Flight.App.exe`.
