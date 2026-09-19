
# Feature: Automatic vocabulary/context building

## What it does
Background process that extracts vocabulary/context terms from confirmed dictations (feature #1's `DictationSession` rows) into the local `VocabularyEntry` store, then feeds relevant terms back into the LLM cleanup step of future dictations to improve recognition of client names, jargon, and product terms — with no manual dictionary setup required.

## Acceptance criteria
- When a `DictationSession` is confirmed, a background job extracts candidate terms from `cleaned_output` without blocking the dictation loop's UI — extraction happens after clipboard copy, not before.
- Extracted terms are written to `VocabularyEntry` (term, embedding, frequency, last_used_at, source): new terms create a row, repeated terms increment frequency and update `last_used_at` rather than duplicating.
- No UI exists for manually adding a vocabulary entry — every entry originates from a confirmed dictation.
- Before the LLM cleanup step runs on a new dictation, the system retrieves the most semantically-relevant `VocabularyEntry` rows (via sqlite-vec similarity) and includes them in the LLM's context.
- If the vocabulary store is empty (no history yet), the loop still functions normally with no context injected — empty store is not an error state.
- Vocabulary extraction/injection never touches the clipboard directly — it only affects what the LLM produces as `cleaned_output`, which still goes through feature #1's preview-and-confirm step.
- A dictation confirmed while cloud LLM mode (feature #4) is active still extracts and stores vocabulary the same way local mode does — extraction is independent of which LLM produced the cleanup.

## Out of scope for this feature
- Manual vocabulary review/edit/delete UI — no visibility screen this pass.
- Term-extraction quality tuning or dedup beyond exact-string matching — revisit once there's real usage data from the validation period.
- Injecting vocabulary into the STT step itself (whisper.cpp hotwords/initial_prompt) — this feature only wires vocabulary into the LLM cleanup context; biasing transcription is a separate, later optimization if needed.
- Cross-device sync of vocabulary — local store only, per device; no Supabase involvement (accounts don't exist yet per build sequence).
- Translation-aware vocabulary — feature #3 dependency, treat as English-only for this pass.

## Notes for implementation
- Term extraction method (rule-based heuristics vs a lightweight local-LLM extraction pass) isn't specified in the PRD. Since the LLM is already running locally for cleanup, reusing it for extraction is probably lower-effort than a second NLP pipeline — worth considering first in Plan Mode.
- Retrieval count and similarity threshold for context injection aren't specified — start conservative (e.g. top 5-10 above a similarity floor) to avoid bloating the prompt and slowing local inference; tune against real sessions.
- Depends on feature #1's `DictationSession` storage existing and being populated with real transcripts, not synthetic test data — matches the PRD's own reasoning for the dependency.
- sqlite-vec was set up as schema-readiness in feature #1 but unused there — this is where it's first actually queried.