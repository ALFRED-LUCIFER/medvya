// @vitest-environment jsdom
import { afterEach, describe, expect, it } from 'vitest';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import axe from 'axe-core';
import { ToothChart, toothRows } from './index';

afterEach(cleanup);
const noop = () => {};

describe('ToothChart (MDV-P8-011)', () => {
  it('draws FDI teeth facing the patient: the patient\'s right (quadrants 1 and 4) on the viewer\'s left, in every dentition', () => {
    expect(toothRows('permanent')).toEqual([
      [['18', '17', '16', '15', '14', '13', '12', '11'], ['21', '22', '23', '24', '25', '26', '27', '28']],
      [['48', '47', '46', '45', '44', '43', '42', '41'], ['31', '32', '33', '34', '35', '36', '37', '38']],
    ]);
    expect(toothRows('primary')).toEqual([[['55', '54', '53', '52', '51'], ['61', '62', '63', '64', '65']], [['85', '84', '83', '82', '81'], ['71', '72', '73', '74', '75']]]);
    expect(toothRows('mixed').flat(2)).toHaveLength(52); // 32 permanent and 20 primary
  });

  it('keeps the chart left to right inside a right-to-left page, so the quadrants never mirror', () => {
    const { container } = render(<div dir="rtl"><ToothChart dentition="permanent" marks={{}} notRecorded="Not recorded" selected={null} onSelect={noop} /></div>);
    expect(container.querySelector('[data-slot=tooth-chart]')!.getAttribute('dir')).toBe('ltr');
    expect([...container.querySelectorAll('[data-tooth]')].slice(0, 2).map(b => b.getAttribute('data-tooth'))).toEqual(['18', '17']);
  });

  /** SAFETY (mutation check: drop the code printed on a recorded tooth, or the "not recorded" words, and this fails: colour would stand alone). */
  it('says each tooth\'s state in words and prints its code; an unrecorded tooth is not healthy', () => {
    render(<ToothChart dentition="permanent" marks={{ '36': { abbr: 'C', label: 'Caries (DO)', tone: 'danger' } }} notRecorded="Not recorded" selected="36" onSelect={noop} />);
    const caries = screen.getByRole('button', { name: '36 C, Caries (DO)' });
    expect(caries.textContent).toBe('36 C');
    expect(caries.getAttribute('aria-pressed')).toBe('true');
    const blank = screen.getByRole('button', { name: '46, Not recorded' });
    expect(blank.textContent).toBe('46–');
    expect(blank.getAttribute('aria-pressed')).toBe('false');
  });

  /** SAFETY (mutation check: swap the mesial and distal zones, or stop mirroring the lower left quadrant, and this fails: a caries would be drawn on the wrong side). */
  it('paints only the recorded surfaces, on the side they are on for the viewer facing the patient, and prints them under the tooth', () => {
    const { container } = render(<ToothChart dentition="permanent" marks={{ '36': { abbr: 'C', label: 'Caries (DO)', tone: 'danger', surfaces: 'DO', fill: 'hatch' } }} notRecorded="Not recorded" selected={null} onSelect={noop} />);
    const tooth = screen.getByRole('button', { name: '36 C·DO, Caries (DO)' });
    expect(tooth.textContent).toBe('36 C·DO');
    const painted = [...tooth.querySelectorAll('[data-surface]')].filter(p => (p as SVGElement).style.fill.startsWith('url(')).map(p => p.getAttribute('data-surface'));
    expect(painted.sort()).toEqual(['D', 'O']);
    // 36 is lower left: mirrored both ways, so its mesial side faces the midline (the viewer's left) and buccal faces down.
    expect(tooth.querySelector('svg g')!.getAttribute('transform')).toMatch(/scale\(-[\d.]+ -[\d.]+\)/);
    expect(container.querySelector('pattern[id$="-hatch-danger"]')).not.toBeNull();
  });

  it('draws primary 4s and 5s as molars, smaller than the permanent teeth', () => {
    render(<ToothChart dentition="mixed" marks={{}} notRecorded="Not recorded" selected={null} onSelect={noop} />);
    const outline = (n: string) => document.querySelector(`[data-tooth="${n}"] svg g > path`)!.getAttribute('d');
    expect(outline('54')).toBe(outline('16'));
    expect(outline('55')).toBe(outline('17'));
    expect(outline('53')).toBe(outline('13'));
  });

  it('is one tab stop: arrows move along and across the arches, two digits jump and select', () => {
    const picked: string[] = [];
    render(<ToothChart dentition="permanent" marks={{}} notRecorded="Not recorded" selected={null} onSelect={t => picked.push(t)} />);
    const tabbable = [...document.querySelectorAll('[data-tooth]')].filter(b => b.getAttribute('tabindex') === '0');
    expect(tabbable.map(b => b.getAttribute('data-tooth'))).toEqual(['18']);
    const at = (n: string) => document.querySelector(`[data-tooth="${n}"]`) as HTMLButtonElement;
    fireEvent.keyDown(at('18'), { key: 'ArrowRight' });
    expect(document.activeElement).toBe(at('17'));
    fireEvent.keyDown(at('17'), { key: 'ArrowDown' });
    expect(document.activeElement).toBe(at('47'));
    fireEvent.keyDown(at('47'), { key: 'End' });
    expect(document.activeElement).toBe(at('38'));
    fireEvent.keyDown(at('38'), { key: '3' });
    fireEvent.keyDown(at('38'), { key: '6' });
    expect(document.activeElement).toBe(at('36'));
    expect(picked).toEqual(['36']);
    fireEvent.click(at('11'));
    expect(picked).toEqual(['36', '11']);
  });

  it('has no axe violations', async () => {
    const { container } = render(<ToothChart dentition="mixed" marks={{ '36': { abbr: 'F', label: 'Filling', tone: 'info' } }} notRecorded="Not recorded" selected="36" onSelect={noop} />);
    const result = await axe.run(container, { rules: { 'color-contrast': { enabled: false } } });
    expect(result.violations.map(v => v.id)).toEqual([]);
  });
});
