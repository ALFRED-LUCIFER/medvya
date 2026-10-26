// @vitest-environment jsdom
import { describe, expect, it } from 'vitest';
import { render } from '@testing-library/react';
import axe from 'axe-core';
import { I18nProvider } from '@medvya/i18n';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import {
  alertLooks, ClinicalAlert, IdentityBanner, provenanceLooks, ProvenanceTag,
  RecordingPill, recordingLooks, scheduleLooks, frontDeskLooks, StatusChip, statusLooks, SystemState, systemStates, type Look,
} from './primitives';
import { Button } from './components/ui/button';
import { Input } from './components/ui/input';
import { Select, SelectTrigger, SelectValue } from './components/ui/select';
import { Textarea } from './components/ui/textarea';

const ui = (n: React.ReactNode) => render(<>{n}</>);
// Design tokens are defined once, in styles.css; read them so the test checks what ships.
const tokens = Object.fromEntries([...readFileSync(join(process.cwd(), 'src/styles.css'), 'utf8').matchAll(/--color-([\w-]+):\s*(#[0-9a-fA-F]{6})/g)].map(m => [m[1], m[2]]));
// Visible text without the invisible Unicode isolates used around embedded IDs.
const visible = (e: HTMLElement) => (e.textContent ?? '').replace(/[\u2068\u2069]/g, '').replaceAll('\u00A0', ' ');

// Looks may name a token (var(--color-x)) instead of a hex value; resolve it to what ships.
const hex = (c: string) => (c.startsWith('var(--color-') ? tokens[c.slice('var(--color-'.length, -1)]! : c);

function contrast(a: string, b: string) {
  [a, b] = [hex(a), hex(b)];
  const lum = (hex: string) => {
    const [r, g, bl] = [1, 3, 5].map(i => parseInt(hex.slice(i, i + 2), 16) / 255)
      .map(c => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4));
    return 0.2126 * r + 0.7152 * g + 0.0722 * bl;
  };
  const [hi, lo] = [lum(a), lum(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
}

const allLooks: [string, Look][] = [statusLooks, provenanceLooks, alertLooks, recordingLooks, scheduleLooks, frontDeskLooks].flatMap(o => Object.entries(o));

describe('clinical primitives', () => {
  it('meets WCAG AA text contrast (4.5:1) for every state', () => {
    for (const [name, l] of allLooks) expect(contrast(l.fg, l.bg), name).toBeGreaterThanOrEqual(4.5);
    // Unavailable time is a hatch of two tokens: its words must read on both stops, not only on the look's bg.
    for (const stop of ['muted', 'line']) expect(contrast(scheduleLooks.unavailable.fg, tokens[stop]!), `unavailable on ${stop}`).toBeGreaterThanOrEqual(4.5);
    // Filled buttons (white text) and outlined/link text on the canvas.
    for (const bg of ['primary', 'brand-800', 'ai-600', 'ai-700', 'destructive', 'brand-950'])
      expect(contrast('#FFFFFF', tokens[bg]), `white on ${bg}`).toBeGreaterThanOrEqual(4.5);
    for (const fg of ['foreground', 'muted-foreground', 'action-600', 'primary'])
      expect(contrast(tokens[fg], tokens['background']), `${fg} on background`).toBeGreaterThanOrEqual(4.5);
    // Queue TV text on its page and on its panels (MDV-P1-031), white focus ring on both.
    for (const fg of ['card', 'tv-heading', 'tv-muted', 'tv-note']) for (const bg of ['tv-bg', 'tv-panel', 'brand-950'])
      expect(contrast(tokens[fg]!, tokens[bg]!), `${fg} on ${bg}`).toBeGreaterThanOrEqual(4.5);
    // Navy navigation text on the sidebar and on the hovered/current item.
    for (const fg of ['nav-ink', 'nav-muted']) for (const bg of ['brand-950', 'nav-hover'])
      expect(contrast(tokens[fg], tokens[bg]), `${fg} on ${bg}`).toBeGreaterThanOrEqual(4.5);
  });

  it('meets WCAG 1.4.11 non-text contrast (3:1) for control boundaries and the focus ring', () => {
    // Every surface a control sits on or fills with (outline-button hover is accent). Only the light theme ships today.
    const surfaces = ['background', 'surface', 'card', 'popover', 'secondary', 'muted', 'accent'];
    // input: shadcn Input/Select/Textarea and outline Button; line-strong: public-site inputs and outline button.
    for (const boundary of ['input', 'line-strong', 'ring']) for (const bg of surfaces)
      expect(contrast(tokens[boundary]!, tokens[bg]!), `${boundary} on ${bg}`).toBeGreaterThanOrEqual(3);
    // The controls draw that boundary, and keep the global focus ring instead of a faint halo.
    const { container } = ui(<>
      <Input /><Textarea /><Select><SelectTrigger><SelectValue /></SelectTrigger></Select><Button variant="outline">x</Button>
    </>);
    const controls = container.querySelectorAll('input, textarea, button');
    expect(controls).toHaveLength(4);
    for (const c of controls) {
      expect(c.classList, c.outerHTML).toContain('border-input');
      expect(c.classList, c.outerHTML).not.toContain('outline-none');
    }
  });

  it('never conveys status by colour alone: each state has words and a shape cue', () => {
    const { container } = ui(<>
      {Object.keys(statusLooks).map(s => <StatusChip key={s} state={s as keyof typeof statusLooks} />)}
      {Object.keys(provenanceLooks).map(s => <ProvenanceTag key={s} source={s as keyof typeof provenanceLooks} />)}
      {Object.keys(recordingLooks).map(s => <RecordingPill key={s} state={s as keyof typeof recordingLooks} elapsedSeconds={252} />)}
    </>);
    const pills = [...container.querySelectorAll('span')].filter(e => e.parentElement?.tagName !== 'SPAN');
    for (const p of pills) {
      expect(p.textContent?.trim().length).toBeGreaterThan(0);
      expect(p.querySelector('svg, span[aria-hidden]')).not.toBeNull();
    }
    expect(container.textContent).toContain('REC 04:12');
    expect(container.textContent).toContain('AI draft · not reviewed');
  });

  it('distinguishes "no known allergies" from "allergies not recorded"', () => {
    const base = { name: 'Meera Iyer', age: 54, sex: 'F' as const, mrn: 'DQ-10-4482', dob: '12 Mar 1972' };
    expect(ui(<IdentityBanner patient={{ ...base, allergies: [] }} />).container.textContent).toContain('No known allergies');
    expect(ui(<IdentityBanner patient={{ ...base, allergies: null }} />).container.textContent).toContain('Allergies not recorded');
    expect(visible(ui(<IdentityBanner patient={{ ...base, allergies: [{ name: 'Penicillin', status: 'verified' as const }] }} />).container))
      .toContain('Allergy: Penicillin · verified');
  });

  it('renders translated, right-to-left text in Arabic', () => {
    const { container } = render(<I18nProvider locale="ar" country="AE">
      <ProvenanceTag source="ai" /><RecordingPill state="recording" elapsedSeconds={252} /></I18nProvider>);
    expect(container.textContent).toContain('مسودة ذكاء اصطناعي');
    expect(container.textContent).toContain('04:12');
  });

  it('renders every F-05 system state with one action and the right announcement role', async () => {
    for (const state of Object.keys(systemStates) as (keyof typeof systemStates)[]) {
      const { container, unmount } = ui(<main><SystemState state={state} facility="Iyer Family Clinic" onAction={() => {}} /></main>);
      expect(container.querySelectorAll('button')).toHaveLength(1);
      expect(container.querySelector('h1')?.textContent?.length).toBeGreaterThan(0);
      expect(container.querySelector(state === 'offline' ? '[role=status]' : '[role=alert]')).not.toBeNull();
      const result = await axe.run(container, { rules: { 'color-contrast': { enabled: false } } });
      expect(result.violations.map(v => v.id), state).toEqual([]);
      unmount();
    }
    const noAccess = ui(<SystemState state="noAccess" facility="Iyer Family Clinic" onAction={() => {}} />).container.textContent;
    expect(noAccess).toContain('You don’t have access to this facility.');
    expect(noAccess).toContain('You are signed in to Iyer Family Clinic.');
    const ar = render(<I18nProvider locale="ar" country="AE">
      <SystemState state="notFound" onAction={() => {}} /></I18nProvider>).container.textContent;
    expect(ar).toContain('الصفحة غير موجودة');
  });

  it('has no axe violations', async () => {
    const { container } = ui(<main>
      <IdentityBanner patient={{ name: 'Meera Iyer', age: 54, sex: 'F', mrn: 'DQ-10-4482', dob: '12 Mar 1972',
        allergies: [{ name: 'Penicillin', status: 'verified' as const }], consent: 'given', encounter: 'Encounter 04 Oct 2026 · OPD Room 3', synthetic: true }} />
      <ClinicalAlert severity="critical">Potassium 6.1 mmol/L received 09:12</ClinicalAlert>
      <ClinicalAlert severity="attention">Dose not stated in conversation</ClinicalAlert>
    </main>);
    // color-contrast needs real layout (jsdom has none); contrast is asserted numerically above.
    const result = await axe.run(container, { rules: { 'color-contrast': { enabled: false } } });
    expect(result.violations.map(v => v.id)).toEqual([]);
  });
});
