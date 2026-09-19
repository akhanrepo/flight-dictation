# Feature: Bidirectional translation

## What it does
Extends #1's dictation pipeline with source/target language handling. The user selects a source language before speaking (English, Urdu, Arabic, Spanish, French, or Mandarin), speaks in that language, and the pipeline produces translated text in the target language (English by default) through the same preview-and-confirm flow. Works in reverse too (English source, one of the five as target).

## Acceptance criteria
- Settings UI lets the user select an active source language from: English, Urdu, Arabic, Spanish, French, Mandarin.
- The user can independently configure a target language (defaulting to English), so reverse-direction translation (e.g. English speech to Arabic text) works through the same setting.
- Triggering dictation runs STT using the currently-selected source language — not auto-detected.
- If source language equals target language, the pipeline behaves like feature #1 (cleanup only, no translation) — not an error state.
- When source and target differ, the local LLM cleanup step also translates the transcript into the target language, producing translated + cleaned output.
- The preview toast shows the translated text in the target language, not the raw source-language transcript.
- Confirming copies the translated cleaned text to the clipboard — same confirm/dismiss behavior as feature #1.
- `DictationSession` rows are stored with `source_language` and `target_language` populated (fields already reserved in the data model) for all six languages, not left null.
- Switching source/target language mid-session (without restarting the app) takes effect on the next dictation.

## Out of scope for this feature
- Auto-detection of source language — user selects it manually, per this brief's decision.
- Any language beyond English, Urdu, Arabic, Spanish, French, Mandarin — a 6th+ language is a later addition.
- Mixed-language dictation (code-switching mid-sentence) — single source language per dictation only.
- Vocabulary/context injection for non-English terms — feature #2 stays English-only, per that brief.

## Notes for implementation
- Confirm whether the local LLM chosen in feature #1's Plan Mode can do translation-quality work at the 3–8B class, or whether translation needs a different local model from the one used for English cleanup — a real quality risk worth testing early rather than assuming cleanup-quality implies translation-quality.
- The specific language list and "user selects source" (rather than auto-detect) were decided for this brief, not sourced from the PRD — flag if requirements elsewhere name a different set before building.
- Depends on feature #1's pipeline (hotkey, STT, LLM cleanup, preview, clipboard) as the extension point — this adds language handling on top, it doesn't rebuild the loop.