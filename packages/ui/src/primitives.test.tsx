// @vitest-environment jsdom
import { describe, expect, it } from 'vitest';
import { render } from '@testing-library/react';
import { ThemeProvider } from '@mui/material';
import axe from 'axe-core';
import { medvyaTheme } from './theme';
import {
  alertLooks, ClinicalAlert, IdentityBanner, provenanceLooks, ProvenanceTag,
  RecordingPill, recordingLooks, StatusChip, statusLooks, type Look,
} from './primitives';

const ui = (n: React.ReactNode) => render(<ThemeProvider theme={medvyaTheme}>{n}</ThemeProvider>);

function contrast(a: string, b: string) {
  const lum = (hex: string) => {
    const [r, g, bl] = [1, 3, 5].map(i => parseInt(hex.slice(i, i + 2), 16) / 255)
      .map(c => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4));
    return 0.2126 * r + 0.7152 * g + 0.0722 * bl;
  };
  const [hi, lo] = [lum(a), lum(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
}

const allLooks: [string, Look][] = [statusLooks, provenanceLooks, alertLooks, recordingLooks].flatMap(o => Object.entries(o));

describe('clinical primitives', () => {
  it('meets WCAG AA text contrast (4.5:1) for every state', () => {
    for (const [name, l] of allLooks) expect(contrast(l.fg, l.bg), name).toBeGreaterThanOrEqual(4.5);
    const p = medvyaTheme.palette;
    for (const bg of [p.primary.main, p.primary.dark, p.ai.main, p.ai.dark, p.error.main])
      expect(contrast('#FFFFFF', bg), `white on ${bg}`).toBeGreaterThanOrEqual(4.5); // filled buttons
    expect(contrast(p.primary.main, p.background.default), 'outlined/text button').toBeGreaterThanOrEqual(4.5);
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
    expect(ui(<IdentityBanner patient={{ ...base, allergies: [{ name: 'Penicillin', status: 'verified' }] }} />).container.textContent)
      .toContain('Allergy: Penicillin · verified');
  });

  it('has no axe violations', async () => {
    const { container } = ui(<main>
      <IdentityBanner patient={{ name: 'Meera Iyer', age: 54, sex: 'F', mrn: 'DQ-10-4482', dob: '12 Mar 1972',
        allergies: [{ name: 'Penicillin', status: 'verified' }], consent: 'given', encounter: 'Encounter 04 Oct 2026 · OPD Room 3', synthetic: true }} />
      <ClinicalAlert severity="critical">Potassium 6.1 mmol/L received 09:12</ClinicalAlert>
      <ClinicalAlert severity="attention">Dose not stated in conversation</ClinicalAlert>
    </main>);
    // color-contrast needs real layout (jsdom has none); contrast is asserted numerically above.
    const result = await axe.run(container, { rules: { 'color-contrast': { enabled: false } } });
    expect(result.violations.map(v => v.id)).toEqual([]);
  });
});
