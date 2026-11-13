// @vitest-environment jsdom
import { useState } from 'react';
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import axe from 'axe-core';
import { Card, EmptyState, PageMessage, SegmentedControl, Section, SplitLayout } from './index';

// MDV-P1-086. jsdom has no layout: sizes are checked as classes here and measured by Playwright on /__ui.
afterEach(() => { cleanup(); vi.restoreAllMocks(); });
const cls = (el: Element | null) => (el?.getAttribute('class') ?? '').split(/\s+/);

describe('Card and Section', () => {
  it('has only the section and item sizes, with title, intro and actions slots', () => {
    render(<><Card title="Today" intro="Booked and walk-in" actions={<button>Add</button>}>x</Card><Card size="item" title="A-07">y</Card></>);
    expect(screen.getByRole('heading', { level: 2, name: 'Today' })).toBeTruthy();
    expect(screen.getByText('Booked and walk-in')).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Add' })).toBeTruthy();
    expect(screen.getByRole('heading', { level: 3, name: 'A-07' })).toBeTruthy();
    // @ts-expect-error only 'section' and 'item' exist (AC3): this stops compiling if a third size is added.
    void <Card size="large" />;
  });

  it('a titled section Card is a region named by its title; item cards, Sections and untitled cards are not, and an aria-label wins', () => {
    render(<><Card title="Visits">x</Card><Section title="History">y</Section><Card>z</Card><Card size="item" title="A-07">v</Card><Card title="Upload" aria-label="Upload a document">w</Card></>);
    expect(screen.getByRole('region', { name: 'Visits' })).toBeTruthy();
    expect(screen.getByRole('region', { name: 'Upload a document' })).toBeTruthy();
    expect(screen.getAllByRole('region')).toHaveLength(2);
  });

  it('a headingId names the region and lets the title take focus', () => {
    render(<Card title="Referrals" headingId="referrals">x</Card>);
    const h = screen.getByRole('heading', { name: 'Referrals' });
    expect(h.id).toBe('referrals');
    h.focus();
    expect(document.activeElement).toBe(h);
    expect(screen.getByRole('region', { name: 'Referrals' })).toBeTruthy();
  });

  it('flags a bordered Card nested in a Card in development, and not a Section in a Card', () => {
    const error = vi.spyOn(console, 'error').mockImplementation(() => {});
    const { container } = render(<Card title="Outer"><Section title="Inner section">ok</Section></Card>);
    expect(error).not.toHaveBeenCalled();
    expect(screen.getByRole('heading', { level: 3, name: 'Inner section' })).toBeTruthy();
    expect(container.querySelector('[data-nested]')).toBeNull();
    render(<Card title="Outer"><Card size="item">nested</Card></Card>);
    expect(error).toHaveBeenCalledWith(expect.stringContaining('nested'));
    expect(document.querySelector('[data-slot=card][data-nested]')).not.toBeNull();
  });
});

describe('PageMessage and EmptyState', () => {
  it.each([['busy', 'status'], ['error', 'alert'], ['empty', null], ['denied', null]] as const)('%s shows an icon, a sentence and one action', (kind, role) => {
    const { container } = render(<PageMessage kind={kind} action={<button>Next</button>}>Sentence.</PageMessage>);
    const box = container.firstElementChild!;
    expect(box.getAttribute('role')).toBe(role);
    expect(box.querySelector('svg[aria-hidden]')).not.toBeNull();
    expect(screen.getByText('Sentence.')).toBeTruthy();
    expect(screen.getAllByRole('button')).toHaveLength(1);
    expect(box.getAttribute('aria-busy')).toBe(kind === 'busy' ? 'true' : null);
  });

  it('a title is a bold line above the sentence, not a heading', () => {
    render(<PageMessage kind="error" title="Patients could not be loaded">Nothing was changed.</PageMessage>);
    expect(screen.getByText('Patients could not be loaded').tagName).toBe('P');
    expect(screen.queryByRole('heading')).toBeNull();
  });

  it('a page with its own live region can silence the message', () => {
    const { container } = render(<PageMessage kind="error" role={undefined}>Nothing was changed.</PageMessage>);
    expect(container.firstElementChild!.hasAttribute('role')).toBe(false);
  });

  it('EmptyState is the empty PageMessage', () => {
    const { container } = render(<EmptyState>No patients match.</EmptyState>);
    expect(container.firstElementChild!.getAttribute('data-kind')).toBe('empty');
  });
});

function Views() {
  const [v, setV] = useState<'status' | 'doctor' | 'list'>('status');
  return <SegmentedControl label="View" value={v} onValueChange={setV}
    options={[{ value: 'status', label: 'By status' }, { value: 'doctor', label: 'By doctor' }, { value: 'list', label: 'List' }]} />;
}

describe('SegmentedControl', () => {
  it('is a named group of 44 px buttons; the selected one is pressed', async () => {
    const { container } = render(<Views />);
    expect(screen.getByRole('group', { name: 'View' })).toBeTruthy();
    const [status, doctor] = screen.getAllByRole('button');
    expect(status.getAttribute('aria-pressed')).toBe('true');
    expect(doctor.getAttribute('aria-pressed')).toBe('false');
    expect(cls(doctor)).toContain('h-control');
    fireEvent.click(doctor);
    expect(doctor.getAttribute('aria-pressed')).toBe('true');
    expect(status.getAttribute('aria-pressed')).toBe('false');
    expect((await axe.run(container)).violations).toEqual([]);
  });
});

function stubWide(wide: boolean) {
  const listeners = new Set<() => void>();
  const mql = { matches: wide, addEventListener: (_: string, l: () => void) => listeners.add(l), removeEventListener: (_: string, l: () => void) => listeners.delete(l) };
  window.matchMedia = vi.fn(() => mql) as unknown as typeof window.matchMedia;
  return (next: boolean) => { mql.matches = next; act(() => listeners.forEach(l => l())); };
}

function Split() {
  const [open, setOpen] = useState(false);
  return (
    <SplitLayout side={<p>Allergies</p>} sideLabel="Chart" open={open} onOpenChange={setOpen} closeLabel="Close">
      <button onClick={() => setOpen(true)}>Open chart</button>
    </SplitLayout>
  );
}

describe('SplitLayout', () => {
  it('at 1024 px and wider puts the side beside the main pane', () => {
    stubWide(true);
    render(<Split />);
    expect(screen.getByRole('complementary', { name: 'Chart' }).textContent).toBe('Allergies');
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('under 1024 px opens the side as a Sheet that closes on Escape and returns focus to the opener', async () => {
    const setWide = stubWide(false);
    render(<Split />);
    expect(screen.queryByText('Allergies')).toBeNull();
    const opener = screen.getByRole('button', { name: 'Open chart' });
    opener.focus();
    fireEvent.click(opener);
    const dialog = await screen.findByRole('dialog', { name: 'Chart' });
    expect(dialog.textContent).toContain('Allergies');
    expect(dialog.contains(document.activeElement)).toBe(true);
    fireEvent.keyDown(document.activeElement!, { key: 'Escape' });
    await vi.waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    await vi.waitFor(() => expect(document.activeElement).toBe(opener));
    setWide(true);
    expect(screen.getByRole('complementary', { name: 'Chart' })).toBeTruthy();
  });
});
