# Feature: Cloud LLM toggle

## What it does
Adds a Settings toggle that swaps the LLM cleanup step from local inference to a cloud API call, behind the same interface feature #1 established, for cases where local model quality or speed isn't sufficient.

## Acceptance criteria
- A Settings toggle switches LLM mode between "local" and "cloud" (the `Settings` entity's LLM toggle preference, per the data model).
- When cloud mode is active, cleanup routes the raw transcript to a cloud LLM API instead of the local model, through the same input/output contract feature #1 uses — the rest of the pipeline (preview, confirm, clipboard) is unchanged.
- The cloud model used is budget/cheap-tier (e.g. GPT-4o-mini, Gemini Flash, or DeepSeek-class per Tech constraints), not a frontier/premium model.
- `DictationSession` rows record `LLM_mode="cloud"` with a timestamp for every cloud-processed dictation — this is the only record #5 needs later to compute trial windows; no separate tracking is built here.
- If the cloud API call fails or times out, the user sees an explicit error state — no silent fallback to a stale or empty result.
- After a failed cloud call, the user can manually switch back to local mode to keep dictating.
- Toggling between local and cloud mid-session takes effect on the next dictation, no restart required.
- Cloud mode is unrestricted in this feature — no usage cap, no paywall, no account requirement. Enforcing "free for 30 days, then pay" is entirely feature #5's job, done later against the data this feature already logs.

## Out of scope for this feature
- Any entitlement, trial-clock, or paywall enforcement — deliberately deferred to feature #5. This feature only needs to log what #5 will later read.
- Choosing the specific cloud provider/model — the PRD lists this as an open question, not decided here.
- Cost tracking, usage caps, or budget alerts for cloud API spend.
- Automatic fallback from cloud to local on failure — this brief specifies an explicit error state, not silent failover.

## Notes for implementation
- Cloud provider is an explicit PRD open question — resolve in Plan Mode before implementation.
- Pricing policy (decided): local mode is free forever, unlimited, no account ever required. Cloud mode is free for a 30-day trial from first cloud use, then requires a paid subscription. This feature ships cloud open with no enforcement — real users won't exist in volume until after #5 ships anyway, so the cost-exposure window this created in the earlier draft of this brief is no longer a live concern.
- API keys/secrets for the cloud provider need secure local storage, not plaintext config.