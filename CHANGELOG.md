# Changelog

All notable changes to Flight are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- **Configurable push-to-talk key.** Pick any key or combination in Settings (Right Ctrl, F8, Ctrl+Win, Ctrl+Shift+Space, …). It takes effect immediately, and Right Alt stays the default.
- **Mini recording indicator.** A small pill with a live microphone level while you speak and a spinner while Flight processes. It never takes focus.
- **AI cleanup setting.** Turn LLM cleanup off for the fastest path (whisper plus rule-based tidying). Translation always uses the model.
- **Timings log.** `%LOCALAPPDATA%\Flight\logs\timings.log` records where each dictation's time went.
- Open-source project files: README, contributing guide, code of conduct, security policy, issue and PR templates, architecture docs, CodeQL and Dependabot.

### Changed
- **Text goes straight to the clipboard.** The preview/confirm step is gone: release the key, then paste.
- **Faster dictation.** Models warm up in the background at launch; vocabulary retrieval no longer runs the LLM; the local LLM's context is sized to each request; phrases under four words skip the LLM; background vocabulary learning waits until a dictation finishes; whisper keeps its processor between dictations.
- Cloud mode is bring-your-own-key and needs no account.
- Updates come from GitHub Releases.
- `Flight.App --benchmark` reports cold and warm runs.

### Removed
- Accounts, subscription and entitlement (Supabase and Stripe). Flight is free and open source.

### Fixed
- Silence no longer puts `[BLANK_AUDIO].` on the clipboard: silent or marker-only captures copy nothing and show "Didn't catch that".
- Releasing Alt or Win after dictating no longer opens a menu bar or the Start menu.
- Quitting while a dictation is processing no longer copies its result afterwards.

## [0.1.0] — private builds

The first working versions, built feature by feature from the briefs in [`docs/briefs/`](docs/briefs/): the core dictation loop, automatic vocabulary, translation, the cloud toggle, the tray icon and the visual redesign.

[Unreleased]: https://github.com/akhanrepo/flight-dictation/commits/main
[0.1.0]: https://github.com/akhanrepo/flight-dictation/tree/main/docs/briefs
