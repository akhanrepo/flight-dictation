# Security Policy

## Supported versions

Only the latest release gets security fixes. Installed copies update themselves from GitHub Releases.

## Reporting a vulnerability

**Please don't open a public issue for security problems.**

Report them privately through GitHub: go to the repository's **Security** tab and choose **Report a vulnerability** ([direct link](https://github.com/akhanrepo/flight-dictation/security/advisories/new)). Include:

- what the issue is and its impact,
- steps to reproduce, or a proof of concept,
- the Flight version and Windows version.

You can expect an acknowledgement within a week. Fixes are coordinated in a private advisory, and you'll be credited unless you'd rather not be.

## Scope

Things that especially matter for Flight:

- **Data leaving the machine in local mode.** Local mode must not send audio, transcripts or vocabulary anywhere.
- **The cloud API key.** It must stay encrypted at rest (Windows DPAPI) and only ever be sent to the configured endpoint.
- **The update channel.** Installers are not code-signed yet; anything that lets an attacker substitute an update is in scope.
- **The global keyboard hook.** Flight sees every key event in order to detect the hotkey. It must never record or persist keystrokes.
