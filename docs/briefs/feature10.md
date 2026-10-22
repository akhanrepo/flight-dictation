# Feature: Faster dictation

## What it does
Cuts the time from releasing the key to the text being on the clipboard, especially on the first dictation after launch, on CPU-only machines.

## Where the time went (code analysis)
1. **Cold models.** Whisper and the ~2.5 GB Phi-4 weights loaded lazily on the first dictation, so the first one after launch paid for both loads.
2. **A hidden second inference.** Vocabulary retrieval embedded every transcript with the 3.8B-parameter LLM, which is a full forward pass before cleanup even started.
3. **Oversized context.** Every cleanup call allocated a fresh 4096-token KV cache (~0.5 GB for Phi-4-mini), however short the transcript.
4. **LLM on trivial input.** "Yes, thanks." went through a full LLM pass.
5. **CPU contention.** Background vocabulary extraction (an LLM call) could run at the same time as the next dictation.
6. **Whisper defaults.** The thread count was left at the default and the processor was rebuilt on every dictation.

## Acceptance criteria
- After the models are ready, Flight warms up in the background: Whisper always, and the local LLM (a load plus a one-token inference) only when cleanup runs locally. Warm-up runs once, never blocks dictation, and a failure is logged rather than shown. Switching to local mode later warms the LLM then.
- Vocabulary retrieval uses the model-free trigram embedding, so the dictation path never runs the LLM for retrieval. Rows stored with the old LLM embeddings are re-embedded once.
- The local LLM context is sized to the request (prompt + output budget, rounded up to 512, capped at 4096) instead of a fixed 4096.
- When not translating, a transcript under four words is tidied by rules without an LLM call.
- A new **AI cleanup** setting (default on). When off, and not translating, the transcript is tidied by rules only. Translation always uses the LLM.
- Background vocabulary learning waits while a dictation is capturing or processing.
- Every dictation appends its stage timings (audio length, speech-to-text, cleanup, total) to `logs/timings.log`, which keeps the last 200 lines.

## Out of scope (recommended follow-ups, each a quality trade-off to A/B first)
- A smaller default LLM (Qwen2.5-1.5B or Llama-3.2-3B).
- A GPU (Vulkan) backend.
- Caching the system prompt's KV prefix across calls.

## Notes for implementation
- These are code-level fixes; they couldn't be benchmarked in the dev container (no model downloads). `timings.log` and `Flight.App --benchmark` are how to measure them on real hardware.
