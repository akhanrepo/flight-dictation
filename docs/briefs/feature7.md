# Feature: Visual design: a quiet, minimal Flight

## What it does
Redesigns Flight's three windows (the dictation toast, Settings, Account) so they look like a calm, native Windows 11 utility: compact, light, slightly translucent, movable, and never in the way. Flight is used dozens of times a day in the middle of other work, so the toast in particular should feel like a whisper, not a dialog.

### Why the current toast breaks
The toast is sized in physical pixels (520×300). At 150% display scaling that is ~347×200 layout units, so the header and button rows consume all the height: the preview text row collapses to zero (the dictated text is invisible), the title runs under the Settings button, and the Copy button is clipped. Every size in this feature is specified in layout units (DIPs) and converted for the monitor's scale.

## Design

**Principles:** content first (the dictated text is the hero; chrome recedes) · one primary action per state · native Windows 11 Fluent look (system font, theme colors, light/dark from the OS) · an 8-px spacing grid · the toast never steals more than it gives back.

**Tokens**
| Token | Value |
|---|---|
| Font | Segoe UI Variable (system): body 14, caption 12, title 14 semibold |
| Spacing | 4 / 8 / 12 / 16 |
| Corner radius | 8 (cards, windows), 4 (buttons) |
| Compact button | height 28, horizontal padding 12, font 12 |
| Icon button | 28×28, 14-px glyph, tooltip and accessible name |
| Toast width | 400 (card), 280 (pill) |
| Toast height | fits content: min 52, max 320; longer text scrolls inside |
| Backdrop | Desktop Acrylic, tuned so the window behind shows through faintly while text keeps >= 4.5:1 contrast; solid automatically when Windows transparency effects are off |

**Toast states**
- *Listening:* a pill with a red pulsing dot, "Listening", and a muted hint "Release Right Alt to finish".
- *Processing:* a pill with a small spinner and "Cleaning up…".
- *Preview:* a card. Header: a 16-px logo, the caption "Preview", and icon buttons on the right for Settings (gear) and Close (×). Body: the dictated text, selectable, scrolling if long. Footer: the muted hint "Enter to copy · Esc to dismiss" on the left, with compact **Copy** (accent) and **Dismiss** on the right.
- *Copied:* the card briefly shows "Copied to clipboard" with a check, then fades out (about 1 s).
- *Error:* a card with a warning glyph, a short title, the message, and **Dismiss**.
- *Cloud needs a subscription:* a card with the message, **Subscribe** (accent) and **Keep using local**.
- *First-run download:* a pill with a thin progress bar and "Setting up · downloading model 42%".

**Settings and Account windows:** compact (Settings 440×520, Account 420×480 DIPs), a Mica backdrop, the logo and title in a custom title bar, and settings grouped into rounded cards with a label and description on the left and the control on the right. No redundant Close button (the title bar has one).

## Acceptance criteria

### Layout and sizing
- Toast, Settings and Account sizes are defined in DIPs and converted with the monitor's scale factor, so they look the same at 100%, 125%, 150% and 200%.
- The toast's height fits its content, between 52 and 320 DIPs; longer preview text scrolls inside the card instead of growing past the cap or being cut off.
- No text overlaps any other element in any toast state. Titles are a single line and trim with an ellipsis, and action buttons always have room.
- Buttons in the toast are compact (28 DIPs high) and the header uses icon buttons, not text buttons.

### Movable, and out of the way
- The user can drag the toast by its background to anywhere on screen.
- Flight remembers where the toast was dragged and uses that position next time, including after a restart.
- A remembered position that is now off-screen (for example after unplugging a monitor or changing resolution) is pulled back fully inside the nearest work area.
- With no remembered position, the toast sits centered at the bottom of the primary work area, 24 DIPs above the taskbar.
- Listening, Processing and download states never take keyboard focus from the app the user is working in.
- When the preview takes focus (so Enter and Esc work) and the user copies or dismisses, focus goes back to the window they were in before, so they can paste straight away.

### Look
- The toast uses a translucent acrylic backdrop: the window behind is faintly visible, but text stays readable. It falls back to solid when Windows transparency effects are off.
- All three windows follow the Windows light/dark theme and accent color.
- The toast shows each state as specified above, including a brief "Copied to clipboard" confirmation after copying.
- Icon-only buttons have tooltips and accessible names. Enter and Esc keep working in the preview.

## Out of scope for this feature
- Custom themes, user-chosen transparency, or animations beyond the listening pulse and the copied fade.
- Hotkey remapping and any new Settings options.
- A main window or history view.
- Changes to the tray menu (feature 6).

## Notes for implementation
- What the toast shows (state, title, body, hint, actions), sizing maths, placement and clamping, the remembered position and the focus-return decision all live in `Flight.Core` and are unit-tested. The WinUI windows only render.
- **Manual-verification criteria** (rendering can't be unit-tested without a UI harness, which CLAUDE.md rules out): the acrylic look and its readability, the light/dark appearance, and the absence of overlap at 100/150/200% scaling. These are checked on the installed app with screenshots attached to the commit summary.
