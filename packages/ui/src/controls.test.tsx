// @vitest-environment jsdom
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

// jsdom has no layout, so the 44 px defaults are checked as the classes that produce them; Playwright measures them on /__ui.
const cls = (el: Element | null) => (el?.getAttribute('class') ?? '').split(/\s+/);

describe('library control defaults (MDV-P1-077)', () => {
  it('Input, Select trigger and Textarea are at least 44 px with no class added', () => {
    const { container } = render(<><Input /><Select><SelectTrigger><SelectValue /></SelectTrigger></Select><Textarea /></>);
    expect(cls(container.querySelector('[data-slot=input]'))).toContain('h-11');
    expect(cls(container.querySelector('[data-slot=select-trigger]'))).toContain('data-[size=default]:h-11');
    expect(cls(container.querySelector('[data-slot=textarea]'))).toContain('min-h-16'); // 64 px
  });

  it('every Button size is at least 44 px tall, so a call site never patches it (MDV-P1-078)', () => {
    const { getAllByRole } = render(<><Button>a</Button><Button size="sm">b</Button><Button size="icon" aria-label="c" /></>);
    const [base, sm, icon] = getAllByRole('button').map(cls);
    expect(base).toContain('min-h-11');
    expect(sm).toContain('min-h-11');
    expect(icon).toContain('size-11');
  });

  it('Switch is a 44 by 44 px target around an unchanged 32 by 18 px track', () => {
    const { getByRole, container } = render(<Switch aria-label="Reminders" />);
    expect(cls(getByRole('switch'))).toContain('size-11');
    const track = cls(container.querySelector('[data-slot=switch-track]'));
    expect(track).toContain('w-8');
    expect(track).toContain('h-[1.15rem]');
  });

  it('ChoiceRow makes the whole 44 px row the target of a native input', () => {
    const { getByRole, container } = render(<ChoiceRow type="checkbox">Send a reminder</ChoiceRow>);
    const box = getByRole('checkbox', { name: 'Send a reminder' }) as HTMLInputElement;
    expect(cls(container.querySelector('label'))).toContain('min-h-11');
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
});
