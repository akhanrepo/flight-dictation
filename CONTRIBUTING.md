# Contributing to Flight

Thanks for your interest! Flight is a small project with a few firm conventions. They're what keep it testable without a UI harness and without downloading 2.5 GB of models, so please read this before opening a pull request.

## Ways to help

- **Report a bug:** open an issue with the bug template. Attach `%LOCALAPPDATA%\Flight\logs\errors.log` (and `timings.log` for speed problems) if they're relevant.
- **Suggest a feature:** start a [Discussion](https://github.com/akhanrepo/flight-dictation/discussions) or open a feature request. Larger features get a written brief first (see below).
- **Pick up an issue:** look for `good first issue` or `help wanted`, and comment so nobody duplicates the work.
- **Improve docs:** always welcome, and no brief needed.

## Development setup

| You want to… | You need |
|---|---|
| Work on Core (logic, tests) | .NET 8 SDK or newer, on Windows, Linux or macOS |
| Run the app | Windows 10 1809+ / 11, x64 |

```bash
git clone https://github.com/akhanrepo/flight-dictation.git
cd flight-dictation

# Windows: everything
dotnet build Flight.slnx
dotnet test Flight.slnx

# Linux/macOS: Core and tests (the WinUI shell needs Windows to build)
dotnet test tests/Flight.Tests/Flight.Tests.csproj
```

The first app launch downloads the models into `%LOCALAPPDATA%\Flight\models`. Tests never touch models or the network.

## Architecture rules

Read [docs/architecture.md](docs/architecture.md) for the whole picture. The rules that matter most in review:

1. **Logic lives in `Flight.Core`.** `Flight.App` is a thin WinUI shell: it renders view models and forwards OS events. If you're writing an `if` in a `.xaml.cs` file, it probably belongs in Core.
2. **Every OS and model seam is an interface** (`IKeyboardHook`, `IAudioCapturer`, `ISpeechToText`, `ILlmCompletion`, `IClipboardWriter`, …) with a fake in `tests/Flight.Tests/Fakes/Fakes.cs`. Reuse those fakes.
3. **All services are registered in `Flight.Core.Hosting.AppHost`**; the app and the tests boot the same host.
4. **The clipboard is written in one place:** `DictationOrchestrator`, after a successful non-empty result, followed by persist and then vocabulary learning. Don't add another path.
5. **Never size windows in raw pixels.** Sizes are DIPs, converted per monitor (`ToastLayout`).
6. **Nullable reference types are on.** No `!` suppressions without a comment saying why it's safe.

## Tests

- Tests mirror the source tree: `src/Flight.Core/Input/Hotkey.cs` → `tests/Flight.Tests/Input/HotkeyTests.cs`.
- Names are `<Subject>_<Behavior>` facts or theories.
- Prefer testing behaviour through public interfaces and the DI container over internals.
- **No test may need a downloaded model, a network connection or Windows.** CI runs the suite on Linux as well as Windows.
- `dotnet test` must pass before every commit.

## How features are built

Each feature starts as a short brief in [`docs/briefs/`](docs/briefs/): what it does and why, the design, numbered **acceptance criteria**, and what's out of scope. Then:

1. Agree the brief (in an issue or Discussion).
2. Plan the implementation before writing code.
3. **Every acceptance criterion maps to at least one test.** If a criterion can't be tested automatically (visual design, for example), the brief says so and says how it will be checked manually.

Bug fixes and small improvements don't need a brief; a test that reproduces the bug is enough.

## Pull requests

- Keep each PR to one thing, made of small coherent commits.
- Commit messages: an imperative subject line (≤ 72 chars), then a body explaining *why*.
- Fill in the PR template. For UI changes, attach screenshots at 100% and 150% scaling.
- Update `CHANGELOG.md` under **Unreleased** for anything user-visible.
- Update `CLAUDE.md` and `docs/architecture.md` when an architecture decision changes. (`CLAUDE.md` is the project guide for AI coding assistants; keeping it accurate keeps their help accurate.)

## Releasing (maintainers)

Tag `vX.Y.Z` on `main`. The release workflow tests, publishes a self-contained build, packs it with Velopack and publishes a GitHub Release. Installed copies update from there.

## Code of Conduct

This project follows the [Contributor Covenant](CODE_OF_CONDUCT.md). By participating you agree to uphold it.
