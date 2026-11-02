# Backlog

Candidate features that aren't scheduled yet. Each one becomes a brief in [`briefs/`](briefs/) before anyone builds it (see [CONTRIBUTING.md](../CONTRIBUTING.md)). Nothing here is a commitment.

Most of these ideas came from looking at Hex, another push-to-talk dictation app, in November 2026. Each one is judged against Flight's goals: private, CPU-only, a vocabulary that learns, and clipboard-only output (see the non-goals in the [PRD](PRD.md)).

## How items are classified

| Class | Meaning |
|---|---|
| **Next** | High value, fits the current architecture, small to medium effort. A brief can be written now. |
| **Soon** | Clearly useful, but either smaller payoff or more design work first. |
| **Later** | Worth doing eventually. Today it's blocked on cost, hardware or a bigger change. |
| **No** | Conflicts with a non-goal or with how Flight works. Listed so it isn't proposed again without a reason. |

## Next

### Vocabulary biasing for speech-to-text
Pass the most relevant learned terms (client names, product names, jargon) to whisper as its initial prompt, so it hears them right the first time.

*Why:* this is Flight's core promise. Today learned terms only reach the cleanup step, after whisper has already misheard "Kubernetes" as "cooper netties", and cleanup can only fix what's recognisable. Whisper.net supports a prompt (`WithPrompt`), and `IVocabularyContextProvider` already ranks terms, so it's a small change. It doesn't conflict with `WithNoContext`: that stops the previous dictation's text carrying over, which is a different thing from a prompt. It needs a benchmark, though: an overlong prompt makes whisper repeat it back.

### Spoken formatting and symbols
Say "new line", "new paragraph", "open quote", "comma", "colon", "hashtag" or "slash" and get the character instead of the word.

*Why:* it's rule-based, so it costs nothing on CPU. It fits next to `CleanupGuard.FallbackClean`, the quick path that runs when AI cleanup is off or a phrase is under four words. Without it, those fast paths can't produce line breaks or punctuation on request. It's fully unit-testable. It has to run before the LLM, and the LLM prompt must keep the result as it is.

### Double-tap to lock (hands-free mode)
Double-tap the push-to-talk key to keep recording without holding it. A single tap (or a release, after a lock) ends the dictation.

*Why:* holding a key through a two-minute dictation is tiring, and feature 8 explicitly left toggle mode out of scope. It belongs in `PushToTalkHotkey`, which is already a tested state machine over synthetic key events. The pill needs a "locked" state so it's obvious recording continues. Watch the gesture: a fast double tap must not also count as a too-short tap.

### Microphone choice and Bluetooth handling
Let the user pick an input device in Settings (default: follow Windows). Optionally prefer the built-in mic over a Bluetooth headset.

*Why:* this is a real failure we already hit. Capture used to record from device 0, the laptop array, even with a headset plugged in, so every dictation came out silent. That's fixed: capture now follows the Windows default mic. A picker is the remaining half of that fix. When a Bluetooth headset's mic opens, it switches into low-quality call audio, which takes a moment and can silence the start of a recording. That's a likely cause of short dictations coming back "Didn't catch that".

## Soon

### Unload models when idle
Free the LLM (~2.5 GB) and whisper after a period without dictations. The next dictation reloads them.

*Why:* matters on the mid-tier laptops Flight targets. The cost is a slow first dictation after idle, which undoes some of feature 10's warm-up. It should be an opt-in setting with a timeout, and the reload must show in the pill.

### History window and dictation stats
Browse, search and re-copy past dictations, with simple stats (count, words, time saved).

*Why:* every dictation is already stored in `dictation_sessions`, so this is mostly UI. Re-copying an old dictation is a common ask. It must re-copy only: no pasting into apps (see **No** below).

### Copy last dictation again (shortcut)
A second hotkey that puts the last result back on the clipboard.

*Why:* useful after the clipboard has been overwritten. This is the clipboard-only form of a "paste last dictation" shortcut. It needs a second hotkey binding, which feature 8 left out of scope.

### Mute or pause media while dictating
Optionally duck system audio or pause media while recording.

*Why:* background audio hurts recognition and the speech gate. Small with Windows media session APIs. Off by default.

### First-run practice screen
A short guided first dictation (hold the key, say a sentence, see it copied) after the model download.

*Why:* good onboarding, and it doubles as a mic check: it can catch the silent-mic problem above on day one. Mostly UI, so it can follow the mic picker.

## Later

### Live text while dictating
Show provisional words next to the pill as you speak.

*Why:* it needs streaming whisper running repeatedly during capture. On CPU-only hardware that competes with the final pass and makes the overall dictation slower. Revisit with a GPU backend.

### Alternative speech models (e.g. Parakeet) and a model picker
Offer other speech-to-text models, chosen by size, language or accuracy.

*Why:* some newer models are faster or more accurate than whisper base, but they need a different runtime (ONNX) and an engine switch behind `ISpeechToText`. Most don't cover all six of Flight's languages. That's an engine decision for its own brief, backed by a benchmark.

### Switch language with the keyboard layout
Change the source language when the Windows input language changes.

*Why:* convenient for multilingual users, but Flight deliberately makes the source language an explicit choice, never a guess (feature 3). Only worth it as an opt-in mapping, and it needs evidence that people want it.

## No

| Idea | Why not |
|---|---|
| Paste last dictation into the focused app | Typing directly into other apps is a [non-goal](PRD.md). Flight only ever writes the clipboard. "Copy last dictation again" (above) is the clipboard-only form. |
| Terminal paste (Ctrl+Shift+V) | Same reason: it's pasting into other apps. |
| Meetings (record calls, transcripts) | Meeting capture is phase 2 in the PRD and explicitly not built yet. |
| Live chapters, subtitle export (SRT/VTT) | Both depend on meeting capture. |
| Sharing the hotkey with other apps | Flight swallows only the non-modifier keys of its hotkey, and only while it matches (feature 8). Letting other apps see the same key would type characters or trigger their actions mid-dictation. Revisit only with a concrete use case. |
