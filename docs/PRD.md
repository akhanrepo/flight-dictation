# Project Flight — PRD

> **Status update (October 2026): Flight is now open source (MIT).** Three decisions below were reversed:
> - **Open source.** The "closed source" non-goal is withdrawn; see [feature 11](briefs/feature11.md).
> - **No subscription.** Accounts, Stripe billing and cloud entitlement (scope item 6, build step 5) were removed. Cloud mode is bring-your-own-key and free.
> - **No preview step.** The notification-with-preview (scope item 2) became a small listening/processing indicator, and the text goes to the clipboard automatically; see [feature 9](briefs/feature9.md).
>
> The rest of this document is the original PRD, kept as the record of what was planned and why.

## Problem

Knowledge workers who dictate technical/business content have two bad options today: standalone dictation tools (Wispr Flow, SuperWhisper) that produce clean text but forget everything between sessions, or general-purpose notes apps that don't do dictation well. Neither compounds. A consultant who dictates client notes, then joins a call, then drafts a deliverable, gets three disconnected tools with three empty contexts, re-explaining jargon and client names every time.

## Target user (ICP)

Business and technical consultants, solo or in boutique/mid-sized firms. Concretely: a 3-person independent strategy consultancy, a freelance technical/IT consultant doing client audits, a boutique tech advisory firm doing due diligence work. Common trait: heavy daily dictation of technical or business language (client names, industry jargon, product names) that a generic dictation tool mishears every time and a consultant has to keep correcting.

Secondary: technical writers, same workflow fit (technical vocabulary, repeated terms, no shared memory across tools).

Deferred (not MVP): content creators, general knowledge workers.

## Core value proposition

The only dictation tool whose vocabulary and context compound across everything you do, instead of resetting every session like standalone competitors.

## MVP scope — must have

1. System-wide dictation, delivered via clipboard copy (not direct text injection into focused app fields)
2. Notification-with-preview: generated text shown in an overlay/toast before copying, user confirms rather than it landing silently on clipboard
3. Bidirectional translation across dictation for up to 5 languages (e.g. speak Urdu, get English text, and reverse)
4. Local LLM for processing, with an optional cloud LLM toggle (cheap/budget-tier model, provider TBD — see Tech constraints)
5. Automatic local vocabulary/context building — no manual dictionary setup required
6. Lightweight account + subscription entitlement (free tier + paid tier from day one; see Tech constraints)

Lean setup/onboarding relative to SuperWhisper/Wispr Flow is a secondary benefit of the above, not a separate build item.

## MVP build sequence

The 6 scope items above describe product capability, not buildable units — some are one increment, some depend on others existing first. This section breaks them into 5 features in build order, each ready to run through the feature-brief process one at a time.

1. **Core dictation loop** — hotkey trigger, local STT, local LLM cleanup, preview toast, clipboard copy on confirm. Covers scope items 1, 2, and the local half of item 4. No dependencies — build first. This is also the PRD's own recommended validation target: use it personally for two weeks before building anything further.
2. **Automatic vocabulary/context building** — background enrichment of the local store from confirmed dictations. Covers scope item 5. Depends on #1 (needs real transcripts to learn from).
3. **Bidirectional translation** — extends #1's pipeline with source/target language handling for up to 5 languages. Covers scope item 3. Depends on #1.
4. **Cloud LLM toggle** — swaps local processing for a budget cloud model behind the same interface #1 establishes. Covers the cloud half of scope item 4. Depends on #1.
5. **Account + subscription entitlement** (Supabase + Stripe) — covers scope item 6. No technical dependency on 1-4, but deliberately sequenced last: no point gating the product on payment before the two-week validation confirms the local model clears the quality bar.

## Explicit non-goals

- Direct text injection into focused app fields (accessibility APIs, per-app handling) — clipboard-copy is enough to validate the core loop; injection is a hardening pass, not a differentiator
- Meeting capture — roadmap phase 2, needs its own local-transcription pipeline. MVP non-goal only, not a permanent one: see Tech constraints for the architecture provision that keeps this addable without a redesign.
- Document generation / templates — roadmap phase 3, depends on accumulated context from phases 1–2. Same architecture provision applies.
- ~~Open source release — commercial strategy is closed source~~ *(reversed October 2026: Flight is MIT-licensed)*
- Agentic coding integration as a distinct feature — technical dictation quality is a quality bar within core dictation, not a separate build
- Team/multi-seat accounts, SSO, audit logging — buyer is an individual consultant or small firm, not enterprise procurement, for MVP

## Success metric

Weekly retained usage: % of installs still dictating at least once in week 4 after install. This is the number that tells you the core loop is sticky enough to justify building the rest of the roadmap on top of it — ahead of conversion rate, which takes longer to get signal on with a small early user base.

## Key user flows

**Persona: consultant/analyst, primary dictation flow**

1. User triggers dictation via system-wide hotkey from any app
2. User speaks; local STT + LLM produce cleaned, structured text
3. Overlay/toast shows a preview of the generated text
4. User confirms → text copied to clipboard
5. User pastes into their target app (email, doc, CRM) manually

**Persona: consultant/analyst, translation flow**

1. User triggers dictation, speaks in a non-English source language (one of the 5 supported)
2. System detects/uses configured source language, translates during processing
3. Preview shows translated text (target language configurable, e.g. always English)
4. User confirms → copied to clipboard
5. (Reverse direction works the same way, source/target swapped)

**Persona: consultant/analyst, vocabulary compounding (background, no explicit user action)**

1. Every confirmed dictation feeds terms/phrases into local vocabulary/context store
2. Subsequent dictations get better recognition on client names, jargon, and product terms without any manual dictionary entry

**Persona: consultant/analyst, subscription flow**

1. User installs, gets free tier by default (core dictation, capped or limited advanced modes)
2. User hits a paid-gated feature or usage cap, sees upgrade prompt
3. User subscribes via Stripe's own hosted checkout/customer portal
4. App checks entitlement against Supabase, unlocks paid tier
5. Entitlement is cached locally so the app keeps working offline between checks

## Data model (rough sketch)

- **Account** — id, email, subscription tier, entitlement status, Stripe customer id
- **Device** — id, account_id, license/entitlement cache (for offline validation), last_synced_at
- **VocabularyEntry** — term, embedding (sqlite-vec), frequency, last_used_at, source (which dictation it came from)
- **DictationSession** — id, raw_transcript, cleaned_output, source_language, target_language, LLM_mode (local/cloud), timestamp
- **Settings** — account_id or device_id, LLM toggle preference, enabled language pairs

Relationships: Account 1—many Devices; Account/Device 1—many DictationSessions; DictationSession contributes many VocabularyEntries.

Kept extensible on purpose: future MeetingSession and DocumentTemplate entities (phases 2–3) are designed to slot in against the same VocabularyEntry/context store rather than requiring a schema rework — see Tech constraints.

## Tech constraints

- App shell/UI: C# + WinUI 3
- Local STT: whisper.cpp (quantized models)
- Local LLM inference: ONNX Runtime GenAI vs llama.cpp (via LLamaSharp) — decision deferred, see Open questions
- Cloud LLM: optional toggle, targeting a cheap/budget-tier model (e.g. GPT-4o-mini, Gemini Flash, DeepSeek-class), not a premium/frontier model — exact provider TBD, kept behind the same interface as local inference
- Local vocabulary/context storage: SQLite + sqlite-vec for semantic search
- Packaging: MSIX or standard installer with auto-updater (Squirrel.Windows or similar); models downloaded on first run, not bundled
- **Backend (decided)**: Supabase for accounts/auth/Postgres, Stripe's own hosted customer portal for billing — smallest build footprint for a solo build, avoids writing account and billing infrastructure from scratch. Supabase also gives a Postgres instance that can host meeting/document metadata later without standing up new infrastructure.
- **Architecture provision for future phases**: meeting capture and document generation are MVP non-goals, not permanent ones. Keep the VocabularyEntry/context schema and the Supabase API generic enough that MeetingSession and DocumentTemplate entities can be added later without redesigning the core data model or backend.

## Open questions

- Local LLM inference engine: ONNX Runtime GenAI vs llama.cpp/LLamaSharp — pending battle-testing on Phi-4-mini, Qwen2.5, Llama 3.2 (3–8B class) on mid-tier Windows hardware
- Cloud LLM provider — leaning cheap/budget-tier (not Claude/ChatGPT-class), exact provider not yet chosen
- Offline entitlement validation strategy — cache duration, grace period, revocation handling
- First technical risk to resolve before further building: can an 8B-class quantized local model reliably follow structuring/cleanup instructions, and what's real inference latency on non-GPU, mid-tier Windows hardware — this determines feasibility of "quick regenerate" interactions and the ONNX vs llama.cpp decision
- Recommended validation step before heavy build investment: build the thinnest possible dictation-only loop, use it personally on real work for two weeks, compare local-model output quality directly against Claude/ChatGPT on the same transcripts (as a quality ceiling benchmark — this is separate from the cheap-model choice for the production cloud toggle)