# Responsive, dark mode and RTL

Source: Mobile and Tablet (X-01, X-02), Foundations §03, Patient Portal, Patient Registration.

## Breakpoint rules

| Breakpoint | Rule |
|---|---|
| Mobile 360+ | Single column, sticky identity banner, bottom tab bar (Today · Patients · Inbox · Messages · More), primary action pinned above the tabs, 48px targets |
| Tablet 768+ | Icon rail navigation (72px), two-column cards, consult button in the banner. The front-desk board scrolls horizontally |
| Desktop 1280+ | Full sidebar (196–232px, `clamp(196px,22vw,232px)`), three-panel layouts (list · detail · evidence) |
| Widescreen 1600+ | Max content width 1440. Evidence panel stays visible |
| Reduced motion | Waveform and progress animations stop. State is still shown in text |

## Mobile screens designed (X-01)

Doctor Today, Capture (recording), Note review (swipe right to accept, tap to edit, evidence in a bottom sheet with 6 s secure replay), Appointments (agenda with state chips), Patient record (allergy chip never collapses), Wearable pairing (4-digit code match).

**Recording on mobile:** the screen stays awake. The lock screen shows the recording indicator **without PHI**. Status line reads e.g. "Phone mic · 14/14 chunks uploaded · screen stays on".

## Dark mode (X-02)

Same semantics with lighter accents. Canvas `#0F1C2A`, card `#12202E`, ink `#E6EDF3`, secondary ink `#9DB0C2`, line `#22364A`, teal `#2BB3A3`, blue `#6B9BFF`, AI `#A08CF0` (AI stays dashed), red `#F47A86` on `#3A1A22`, ok `#4CC79C` on `#12342B`, warn `#E9B15F` on `#3A2A12`. Red and amber keep their text labels.

**High contrast:** 2px borders, no tints, underlined links. Ships in P5-10.

## RTL and languages

- English, Hindi and Arabic string pipeline from P0 (P0-07). Full RTL from P0 (X5). Malayalam in AI from P6.
- Arabic font: Noto Sans Arabic. Numerals in Manrope where the design shows them, e.g. dates on portal cards.
- Arabic RTL is designed for: UAE portal home (R-01), UAE registration Arabic name field (`dir="rtl"`), clinician mobile Today (UAE).
- Use CSS logical properties. Chevron icons flip (`chevron_right` ↔ `chevron_left`).
- The desktop clinician app is **English only** in the designs. Other locales need translation and legal review of consent texts (Foundations §09).

## Patient portal specifics

17px minimum text. Every action is at least 48px tall. Mobile-first PWA. Bottom tabs: Home · Visits · Records · Messages · Me.
