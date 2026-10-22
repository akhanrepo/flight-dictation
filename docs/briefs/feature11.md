# Feature: Open-source release

## What it does
Turns Flight from a private, closed-source product into a public MIT-licensed project that a stranger can understand, build, run and contribute to.

## Decisions
- **License:** MIT, matching the dependencies (Whisper.net, LLamaSharp, NAudio, Velopack) and the models (whisper, Phi-4-mini).
- **No paywall.** The subscription feature (Supabase accounts, Stripe billing, cloud entitlement) is removed. A client-side gate in an open-source app is trivially forked away, and cloud mode already runs on the user's own API key. Cloud becomes "bring your own key", free. The code remains in git history.
- **Repository name:** `flight-dictation`. The app is still called Flight.
- **Releases:** tagged builds publish the Velopack installer to GitHub Releases, which is also the auto-update feed.

## Acceptance criteria
- No account, billing or entitlement code, backend, or backend workflow remains. Cloud mode works with only an API key.
- The shipped `appsettings.json` contains no personal paths or backend URLs, and points updates at the public GitHub repository.
- The repository has a README (what it is, why, features, privacy, install, build, architecture, contributing, license), LICENSE, CONTRIBUTING, CODE_OF_CONDUCT, SECURITY, CHANGELOG, THIRD_PARTY_NOTICES, issue forms, a pull request template, Dependabot, CodeQL and an architecture document.
- CI runs the Core tests on Linux as well as Windows, and the test suite passes on both (no Windows-only path separators in tests).
- A `vX.Y.Z` tag builds, tests and publishes a GitHub Release with the installer.
- The PRD and CLAUDE.md reflect the open-source direction and the removal of the subscription.

## Out of scope
- Code signing (no certificate yet).
- A project website.
- Rewriting git history.

## Manual steps for the maintainer
Rename the repository, make it public, set the description, topics and social preview, enable Discussions and private vulnerability reporting, protect `main`, and delete the unused Supabase project, Stripe products and `SUPABASE_*` secrets.
