# ADR-010 · UI layer: shadcn/ui + Radix + Tailwind 4 (replaces MUI)

- **Status:** Accepted (founder decision, 21 Oct 2026). **Supersedes** the UI row of ADR-001 and PRD v2 §07 "MUI with the Medvya theme".
- **Resolves** source conflict C-15 (`docs/01-prd/source-conflicts.md`).

## Context

PRD v1 (the October plan) chose shadcn/ui + Tailwind. PRD v2 switched to MUI because the Handoff document shipped an MUI theme excerpt. The founder does not want the product tied to MUI's component runtime. MUI's Material look also had to be overridden almost everywhere to match Medvya's own design, and the MUI 7 → 9 jump showed the upgrade cost of a large vendor component library.

## Decision

- **shadcn/ui** components (Radix primitives, `new-york` style) are copied into `packages/ui/src/components/ui` and **owned by us**. They are added with the shadcn CLI (`packages/ui/components.json`, aliases resolved through `package.json` `imports`).
- **Tailwind CSS 4** with the Medvya design tokens in `packages/ui/src/styles.css` (`@theme static`). This is the single source of colour truth. The contrast test reads it.
- **Radix UI** for accessible behaviour (focus, keyboard, ARIA) in select, dialog, popover, etc.
- **Lucide** icons.
- **Right-to-left:** `<html dir>` from the locale controller plus Tailwind logical utilities (`ms-*`, `ps-*`, `start-*`, `rtl:` variant). No CSS-in-JS RTL plugin.
- Clinical primitives (`IdentityBanner`, `StatusChip`, `ProvenanceTag`, `ClinicalAlert`, `RecordingPill`, `SystemState`) are built on these.
- For data-heavy screens (planned): TanStack Table and react-day-picker (shadcn calendar). The schedule is an own CSS grid (MDV-P1-011): FullCalendar's doctor and resource columns are a paid plugin.

## Consequences

- No UI vendor runtime to upgrade around. Component fixes are local code changes.
- Emotion is gone, so no runtime `<style>` injection. The remaining CSP `style-src 'unsafe-inline'` is only needed for inline `style` attributes (clinical state colours from the look tables) and can be removed later by moving those colours to classes.
- The Handoff MUI theme excerpt stays as design reference only (`docs/02-design/components-theme-copy.md`). Its values live on as Tailwind tokens.
- New components: `npx shadcn@latest add <name>` inside `packages/ui`, then fix the `cn` import to `#lib/utils` if the CLI writes a bare alias. Check the result in the UI gallery (`/__ui`), in English and Arabic.
