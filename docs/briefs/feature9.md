# Feature: Mini recording indicator with automatic copy

## What it does
Replaces the preview card with a small pill indicator, similar to SuperWhisper's. The pill shows that Flight is listening, then processing, and when the text is ready it goes **straight to the clipboard**. There is nothing to confirm: dictate, release, paste.

### Why
A confirmation step on every dictation was friction that users skipped through. The preview also took keyboard focus, so pasting needed a focus round-trip. The pill never takes focus, so the user's app stays active and Ctrl+V works the moment the ✓ appears.

## Design
| State | Pill |
|---|---|
| Listening | red pulsing dot + live microphone level bars |
| Processing | small spinner |
| Copied | ✓, then fades out after about 0.8 s |
| Nothing heard | muted "Didn't catch that", hides after about 1.5 s |
| Error | widens to a one-line message (full text in the tooltip), hides after about 6 s |
| First-run download | progress bar + "Downloading model 42%" |

The pill is 120×36 DIPs (up to 360 wide for messages), acrylic with Windows 11's rounded corners, and has no buttons: the whole surface drags it, and its position is remembered (as in feature 7). It is a non-activating window, so even clicking or dragging it leaves focus where it was.

## Acceptance criteria
- On a successful dictation the cleaned (or translated) text is written to the clipboard without any user action. Then the session is saved, then it is handed to vocabulary learning, in that order.
- The pill never takes keyboard focus in any state.
- Silence, a press shorter than 300 ms, or a transcript made only of whisper non-speech markers (`[BLANK_AUDIO]`, `(music)`, `[Silence]`, …) never touch the clipboard and save nothing. A short tap hides quietly; the others show "Didn't catch that".
- Real text that happens to contain brackets is kept.
- Silent audio (below an energy floor) skips speech-to-text and cleanup entirely.
- An error (timeout, cloud failure) leaves the clipboard untouched and shows the message in the pill.
- Quitting while a dictation is processing abandons it without touching the clipboard.
- While listening, the pill receives the microphone level; outside listening, level updates are ignored.
- Copied, nothing-heard and error states hide themselves.

## Out of scope
- Typing the text into the focused app (still a non-goal: clipboard only).
- A history window, or re-copying an earlier dictation.

## Notes for implementation
- Replaces feature 1's preview and Confirm/Reject, and feature 7's card, actions and focus return. Placement, the remembered position and the DIP sizing from feature 7 stay.
- `DictationOrchestrator` remains the only place the clipboard is written, now after a successful non-empty result rather than in `ConfirmAsync`.
- **Checked manually:** the pill's look and the level animation at 100% and 150% scaling.
