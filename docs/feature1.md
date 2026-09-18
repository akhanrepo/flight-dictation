# Feature: Core dictation loop

## What it does
Global hotkey triggers push-to-talk audio capture from any application. On release, local whisper.cpp transcribes the audio, a local LLM cleans/structures the text, and a preview toast shows the result. On user confirmation, the cleaned text is copied to the system clipboard — nothing is written until confirmed.

## Acceptance criteria
- Holding the configured global hotkey from any focused application starts audio capture, with a visible or audible cue that capture is active.
- Releasing the hotkey stops capture and begins processing (no capture occurs before hotkey-down or after hotkey-up).
- Local STT (whisper.cpp, quantized model) produces a raw transcript from the captured audio.
- The local LLM cleans the raw transcript (filler words removed, punctuation/structure fixed) without altering its semantic content.
- A preview toast/overlay displays the cleaned text before the clipboard is touched.
- Confirming the preview (defined input, e.g. keypress or click) copies the cleaned text to the system clipboard.
- Dismissing/rejecting the preview discards the result; the clipboard is left unmodified.
- The clipboard is never written to unless the user explicitly confirms.
- Each confirmed dictation persists a `DictationSession` record locally (raw_transcript, cleaned_output, timestamp, LLM_mode="local") — no vocabulary extraction yet, just the row future features read from.
- On first run, if local models aren't yet downloaded, the user sees download/init progress rather than a silent failure or hang.
- If STT or LLM processing fails, the user sees an explicit error state, not a silent failure or indefinite hang.
- The full loop (hotkey → capture → STT → LLM → preview → confirm → clipboard) works fully offline, with no account, auth, or network call involved.

## Out of scope for this feature
- Direct text injection into app fields — clipboard-copy only, per PRD non-goal.
- Bidirectional translation / non-English source languages (feature #3). Assume English-only input and output for this pass.
- Automatic vocabulary/context building beyond storing the raw `DictationSession` row (feature #2) — no term extraction, no `VocabularyEntry` writes here.
- Cloud LLM toggle (feature #4) — local-only for this feature.
- Account, subscription, or entitlement gating (feature #5) — this feature ships fully unlocked; the two-week personal validation period runs before any billing exists.
- Hotkey customization UI — ship with one fixed default combo; a settings screen to remap it is a later pass.
- Toggle-mode capture — explicitly push-to-talk only per this brief; don't build both interaction models.

## Notes for implementation
- The local LLM inference engine (ONNX Runtime GenAI vs llama.cpp/LLamaSharp) is still an open PRD question, not a decided constraint. This feature is where that decision actually has to be made — resolve it in Plan Mode before implementation, using the PRD's own benchmark plan (Phi-4-mini / Qwen2.5 / Llama 3.2, 3–8B class, on mid-tier non-GPU Windows hardware) rather than picking arbitrarily.
- Models are downloaded on first run, not bundled with the installer (per Tech constraints) — the first-run flow needs to handle "no models present yet" as a real state, not an edge case.
- Set up SQLite now for `DictationSession` storage. `sqlite-vec` doesn't need to do anything yet, but the schema should exist since feature #2 (vocabulary building) reads directly from these session rows — don't design storage this feature will need to be reworked to support.
- No prior application data exists — this is the first feature built, so there's nothing upstream to integrate with.
- This feature is also the PRD's designated two-week personal-validation vehicle (compare local-model output quality against Claude/ChatGPT on real transcripts as a quality ceiling). It should be stable enough for daily driving, not just a technical proof of the pipeline — that's the actual bar for "does this loop work," not just passing the acceptance criteria above.