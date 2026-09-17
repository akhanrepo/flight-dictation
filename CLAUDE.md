# Project Flight

Windows dictation / second-brain app for consultants. Full product context lives in [docs/PRD.md](docs/PRD.md) — read it before working on any feature.

## Tech stack (from PRD)

- **App shell/UI:** C# + WinUI 3 (Windows App SDK), .NET 8
- **Local STT:** whisper.cpp (quantized models)
- **Local LLM:** ONNX Runtime GenAI vs llama.cpp (LLamaSharp) — decision deferred, pending latency/quality testing
- **Cloud LLM:** optional toggle, budget-tier model, provider TBD; same interface as local inference
- **Storage:** SQLite + sqlite-vec for the local vocabulary/context store
- **Backend:** Supabase (accounts/auth/Postgres) + Stripe hosted checkout/portal
- **Packaging:** MSIX or installer with auto-updater; models downloaded on first run

## MVP scope (build order)

1. Core dictation loop — hotkey, local STT, local LLM cleanup, preview toast, clipboard copy on confirm
2. Automatic vocabulary/context building from confirmed dictations
3. Bidirectional translation (up to 5 languages)
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

## Build/run notes (environment quirks)

- This machine has only the .NET 10 runtime; test project uses `RollForward=LatestMajor` to run net8.0 tests on it.
- `Flight.App` defaults to x64/`win-x64` when built via the solution (WinAppSDK self-contained rejects AnyCPU).
- Build: `dotnet build Flight.slnx` · Test: `dotnet test Flight.slnx`
