# Project Flight

Windows dictation / second-brain app for consultants. Full product context lives in [docs/PRD.md](docs/PRD.md) — read it before working on any feature.

## Tech stack (from PRD)

- **App shell/UI:** C# + WinUI 3 (Windows App SDK), .NET 8
- **Local STT:** Whisper.net 1.9.x (whisper.cpp bindings), model `ggml-base-q5_1.bin` — the **multilingual** base model, not the `.en` variant. Feature #3's five non-English source languages need it.
- **Local LLM:** **Decided: LLamaSharp 0.27.x (llama.cpp), CPU backend**, model Phi-4-mini-instruct Q4_K_M GGUF. Chosen for CPU-only mid-tier hardware. Engines sit behind `ILlmCompletion`; `ITextCleaner` is a consumer of it, not an engine. Run `Flight.App --benchmark` to measure real latency.
- **Audio capture:** NAudio 2.2.1 (3.x requires .NET 9), 16kHz mono
- **Cloud LLM:** optional toggle, budget-tier model, provider TBD; same interface as local inference
- **Storage:** SQLite for the local vocabulary/context store. **sqlite-vec is not used**: it's a native loadable extension needing a per-RID binary, which would put a native download in the test path. Embeddings are float32 BLOBs ranked by cosine similarity inside `SqliteVocabularyRepository` — moving to a `vec0` virtual table later is a change inside that one class.
- **Backend:** Supabase (accounts/auth/Postgres) + Stripe hosted checkout/portal
- **Packaging:** MSIX or installer with auto-updater; models downloaded on first run

## MVP scope (build order)

1. Core dictation loop — hotkey, local STT, local LLM cleanup, preview toast, clipboard copy on confirm
2. Automatic vocabulary/context building from confirmed dictations — **built**
3. Bidirectional translation (up to 5 languages) — **built**
4. Cloud LLM toggle behind the same inference interface as #1
5. Account + subscription entitlement (Supabase + Stripe) — deliberately last

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
- **Languages (feature #3):** `SupportedLanguages.All` is the single source of truth for the six languages. Source language is user-selected, never auto-detected. `SqliteSettingsStore` caches the `settings` row in memory; the orchestrator snapshots `ISettingsStore.Current` at hotkey-release, so a change lands on the *next* dictation and can't affect one in flight. Cleanup and translation are one LLM pass (`PromptTextCleaner`), not two — a second round trip would double CPU inference latency.

## Build/run notes (environment quirks)

- This machine has only the .NET 10 runtime; test project uses `RollForward=LatestMajor` to run net8.0 tests on it.
- `Flight.App` defaults to x64/`win-x64` when built via the solution (WinAppSDK self-contained rejects AnyCPU).
- Build: `dotnet build Flight.slnx` · Test: `dotnet test Flight.slnx`
- On non-Windows dev boxes `Flight.App` can't build (WinUI). Build/test Core only: `dotnet build src/Flight.Core/Flight.Core.csproj` · `dotnet test tests/Flight.Tests/Flight.Tests.csproj`
