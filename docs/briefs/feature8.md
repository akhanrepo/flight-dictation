# Feature: Configurable push-to-talk key

## What it does
Lets the user choose the push-to-talk key in Settings instead of the fixed Right Alt. It can be a single key (Right Alt, Right Ctrl, F8, …) or a combination (Ctrl+Win, Ctrl+Shift+Space). Dictation stays hold-to-talk: hold the key or keys to record, release to finish.

## Design
- Settings gets a **Push-to-talk key** card with a button showing the current key. Clicking it switches to "Press your keys…": the next key combination the user holds becomes the new hotkey once the first key is released. Esc cancels. Keys that would break normal typing are rejected with a reason.
- The new key takes effect immediately, with no restart.
- Modifiers are side-generic in combinations ("Ctrl+Space" matches either Ctrl). A lone modifier must be a right-hand one (Right Alt, Right Ctrl, Right Shift, Right Win), because the left-hand ones are used constantly in normal typing.

## Acceptance criteria
- The default hotkey is Right Alt, so existing installs behave exactly as before, including databases created before this feature existed.
- The hotkey is stored in the settings table and survives a restart.
- Hotkeys round-trip through their text form ("Right Alt", "Ctrl+Win", "Ctrl+Shift+Space").
- Rejected hotkeys: a bare letter, digit, Space, Enter, Esc, Tab or Backspace; a bare left-hand modifier; more than four keys. Allowed alone: F1–F24 and right-hand modifiers.
- Pressed fires once when every key of the hotkey is held, in any order. Auto-repeat doesn't fire it again. An extra non-modifier key held at the same time prevents the match.
- Released fires when any key of the hotkey is released.
- A non-modifier key in the hotkey is swallowed while it is part of the match, so Ctrl+Space never types a space. Modifier keys always pass through to other apps.
- Changing the hotkey applies to the next press without a restart. A dictation already in progress still ends when its original keys are released.
- Recording a hotkey captures the combination held at its peak, completes on the first key-up, can be cancelled with Esc, and reports invalid combinations without saving them. While recording, the key events don't start a dictation.
- The tray tooltip and the first-run notice name the configured key.

## Out of scope
- Toggle (press-to-start, press-to-stop) mode.
- Mouse buttons, and more than one hotkey.

## Notes for implementation
- Matching, recording and validation live in `Flight.Core` (`Input/Hotkey`, `Input/PushToTalkHotkey`) and are unit-tested with synthetic key events. The WinUI shell's low-level keyboard hook only forwards events and swallows what Core tells it to.
- Releasing Alt or Win on its own normally activates a menu or the Start menu. When a hotkey containing either one fires, the hook sends the standard no-op "menu mask" key (VK 0xE8) to suppress that. This is checked manually on Windows.
