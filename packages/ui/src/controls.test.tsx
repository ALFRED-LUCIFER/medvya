// @vitest-environment jsdom
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { fireEvent, render } from '@testing-library/react';
import axe from 'axe-core';
import { Button } from './components/ui/button';
import { Input } from './components/ui/input';
import { Select, SelectTrigger, SelectValue } from './components/ui/select';
import { Textarea } from './components/ui/textarea';
import { Switch } from './components/ui/switch';
import { ChoiceRow } from './components/ui/choice-row';
import { Table } from './components/ui/table';
import { Tabs, TabsContent, TabsList, TabsTrigger } from './components/ui/tabs';

// jsdom has no layout, so the defaults are checked as the token classes that produce them, and the tokens' values are read from
// tokens.css; Playwright measures them on /__ui with a mouse and with touch.
const cls = (el: Element | null) => (el?.getAttribute('class') ?? '').split(/\s+/);
const tokens = readFileSync(join(process.cwd(), '../tokens/src/tokens.css'), 'utf8');
const [fine, coarse] = tokens.split('@media (any-pointer: coarse)');

describe('library control defaults (MDV-P1-077, MDV-P2-062)', () => {
  it('the control tokens are 36 px (small 30) with a mouse and 44 px on a coarse pointer, never under 24 px', () => {
    for (const v of ['--spacing-control: 36px', '--spacing-control-sm: 30px', '--spacing-row: 40px']) expect(fine).toContain(`${v};`);
    for (const v of ['--spacing-control: 44px', '--spacing-control-sm: 44px', '--spacing-row: 48px']) expect(coarse).toContain(`${v};`);
    for (const [, px] of tokens.matchAll(/--spacing-(?:control|row)[\w-]*: (\d+)px/g)) expect(Number(px)).toBeGreaterThanOrEqual(24);
  });

  it('Input, Select trigger and Textarea take the control height with no class added', () => {
    const { container } = render(<><Input /><Select><SelectTrigger><SelectValue /></SelectTrigger></Select><Textarea /></>);
    expect(cls(container.querySelector('[data-slot=input]'))).toContain('h-control');
    expect(cls(container.querySelector('[data-slot=select-trigger]'))).toContain('data-[size=default]:h-control');
    expect(cls(container.querySelector('[data-slot=textarea]'))).toContain('min-h-16'); // 64 px
  });

  it('every Button size reads a control token, so a call site never patches it (MDV-P1-078)', () => {
    const { getAllByRole } = render(<><Button>a</Button><Button size="sm">b</Button><Button size="icon" aria-label="c" /></>);
    const [base, sm, icon] = getAllByRole('button').map(cls);
    expect(base).toContain('min-h-control');
    expect(sm).toContain('min-h-control-sm');
    expect(icon).toContain('size-control');
  });

  it('Switch is a control-sized square target around an unchanged 32 by 18 px track', () => {
    const { getByRole, container } = render(<Switch aria-label="Reminders" />);
    expect(cls(getByRole('switch'))).toContain('size-control');
    const track = cls(container.querySelector('[data-slot=switch-track]'));
    expect(track).toContain('w-8');
    expect(track).toContain('h-[1.15rem]');
  });

  it('ChoiceRow makes the whole control-height row the target of a native input', () => {
    const { getByRole, container } = render(<ChoiceRow type="checkbox">Send a reminder</ChoiceRow>);
    const box = getByRole('checkbox', { name: 'Send a reminder' }) as HTMLInputElement;
    expect(cls(container.querySelector('label'))).toContain('min-h-control');
    fireEvent.click(container.querySelector('label')!);
    expect(box.checked).toBe(true);
  });

  it('Table always names its scroll region, and TypeScript refuses a Table without a label', async () => {
    // @ts-expect-error label is required (AC4): this line stops compiling if it ever becomes optional again.
    const unlabelled = <Table />;
    expect(unlabelled).toBeTruthy();
    const { getByRole, container } = render(<Table label="Queue"><tbody><tr><td>A-07</td></tr></tbody></Table>);
    expect(getByRole('region', { name: 'Queue' }).tabIndex).toBe(0);
    expect((await axe.run(container)).violations).toEqual([]);
  });

  it('Tabs are underlined text; the chosen tab is aria-selected, bold and coloured, not a filled box (MDV-P2-062)', async () => {
    const { getByRole, container } = render(
      <Tabs defaultValue="a"><TabsList aria-label="Panels"><TabsTrigger value="a">Chart</TabsTrigger><TabsTrigger value="b">Past</TabsTrigger></TabsList>
        <TabsContent value="a">one</TabsContent><TabsContent value="b">two</TabsContent></Tabs>);
    const chart = getByRole('tab', { name: 'Chart' });
    expect(chart.getAttribute('aria-selected')).toBe('true');
    expect(getByRole('tab', { name: 'Past' }).getAttribute('aria-selected')).toBe('false');
    expect(cls(chart)).toEqual(expect.arrayContaining(['min-h-control', 'aria-selected:font-bold', 'aria-selected:border-brand-700', 'data-[orientation=horizontal]:border-b-2']));
    expect(cls(chart).some(c => c.includes('bg-'))).toBe(false);
    expect((await axe.run(container)).violations).toEqual([]);
  });
});
