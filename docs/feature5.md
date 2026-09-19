# Feature: Account + subscription entitlement

## What it does
Adds lightweight accounts (Supabase) and subscription billing (Stripe hosted checkout/portal), enforcing the product's actual pricing policy: local LLM usage is free forever with no account required; cloud LLM usage is free for a 30-day trial from first use, then requires a paid subscription. Entitlement is cached locally so the app keeps working offline between checks.

## Acceptance criteria
- Using local-only dictation (feature #1/#2/#3 in local mode) never prompts for an account, ever — no entitlement check runs against local-mode dictations.
- The first time a user's cloud dictation history exists (`DictationSession` rows with `LLM_mode="cloud"`), the app can compute a trial-start date from the earliest such row, whether that row predates this feature shipping or not — early cloud usage isn't reset to a fresh 30 days just because #5 launched later.
- While within 30 days of that trial-start date, cloud mode works with no account required.
- Once 30 days past trial-start, attempting a cloud dictation without an active paid subscription is blocked before the API call is made, with a clear upgrade prompt — the user is not silently charged or silently denied with no explanation.
- Local mode remains fully available and unaffected when the cloud trial expires — expiry blocks cloud calls only, never the app as a whole.
- User can create an account (Supabase auth); the app associates the install with that account via a `Device` row (id, account_id, entitlement cache, last_synced_at).
- User can reach Stripe's hosted checkout from the upgrade prompt and complete a subscription purchase.
- After successful checkout, the app's entitlement check against Supabase reflects the paid tier, and cloud dictation works again.
- Entitlement status (trial-active / trial-expired / paid) is cached locally so cloud-mode availability is still correctly gated when offline, without a live Supabase check on every launch.
- Cached entitlement refreshes against Supabase on a defined interval or trigger, not left permanently stale (exact cache duration is an open PRD question — see notes).
- Cancelling a subscription via Stripe's hosted portal is reflected in the app's entitlement on the next successful sync — cloud access reverts to blocked, local access is unaffected.
- If Supabase is unreachable and no valid cached entitlement exists, cloud mode fails closed (blocked, with a clear "can't verify subscription" message) while local mode keeps working — this is different from the general "fail open to free tier" instinct, because local IS the free tier here and needs no verification at all.

## Out of scope for this feature
- Team/multi-seat accounts, SSO, audit logging — explicit PRD non-goal.
- Building the `MeetingSession`/`DocumentTemplate` entities — only the architecture provision (generic-enough schema) applies now.
- Retry/dunning logic for failed payments beyond what Stripe's hosted portal already handles natively.
- Any changes to feature #4 — this feature reads existing `DictationSession` data, it doesn't require #4 to be modified.

## Notes for implementation
- Trial-start anchor (decided): first `DictationSession` row with `LLM_mode="cloud"`, account-wide once an account exists. If a user has pre-existing local cloud-usage history from before they had an account, back-date the trial to that history rather than starting fresh at signup — otherwise reinstalling resets the trial indefinitely, which undermines the whole point of a trial window. Flag to Ali if a more generous "fresh 30 days at signup" policy is actually preferred — it's simpler to build but easier to abuse.
- Offline entitlement validation strategy (cache duration, grace period, revocation handling) is an explicit PRD open question — decide concrete numbers in Plan Mode.
- No technical dependency on #1–4, but deliberately built last: don't start it before the two-week local-model validation (feature #1) confirms the product is worth gating behind payment at all.
- Supabase schema should stay generic enough to host future `MeetingSession`/`DocumentTemplate` metadata without a rework, per the PRD's architecture provision.