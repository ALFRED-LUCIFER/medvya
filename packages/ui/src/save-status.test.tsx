// @vitest-environment jsdom
import { afterEach, describe, expect, it } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import axe from 'axe-core';
import { cn, SaveStatus, StickyActionBar, type SaveState } from './index';

afterEach(cleanup);

describe('SaveStatus (MDV-P1-082)', () => {
  it.each<[SaveState, string]>([
    [{ state: 'saving' }, 'Saving…'],
    [{ state: 'saved', time: '10:42' }, 'Saved 10:42'],
    [{ state: 'failed' }, 'Not saved. Trying again.'],
  ])('reads %j as "%s" in one polite live region', (status, text) => {
    render(<SaveStatus status={status} />);
    expect(screen.getByRole('status').textContent).toBe(text);
  });

  it('never says Saved without a time', () => {
    // @ts-expect-error a saved state without a time does not compile
    render(<SaveStatus status={{ state: 'saved' }} />);
    expect(screen.getByRole('status').textContent).toBe('');
    cleanup();
    render(<SaveStatus status={{ state: 'saved', time: '' }} />);
    expect(screen.getByRole('status').textContent).toBe('');
  });

  it('keeps the same live region across changes, so each change is announced once', () => {
    const { rerender } = render(<SaveStatus />);
    const region = screen.getByRole('status');
    expect(region.textContent).toBe('');
    rerender(<SaveStatus status={{ state: 'saving' }} />);
    rerender(<SaveStatus status={{ state: 'saved', time: '10:42' }} />);
    expect(screen.getByRole('status')).toBe(region);
    expect(region.textContent).toBe('Saved 10:42');
    // Opacity only: no slide or zoom class that would move the text.
    expect(region.innerHTML).toContain('fade-in');
    expect(region.innerHTML).not.toMatch(/slide-in|zoom-in/);
  });
});

describe('cn', () => {
  it('keeps a type token beside a text colour, and lets a later type token replace it', () => {
    expect(cn('text-body text-muted-foreground')).toBe('text-body text-muted-foreground');
    expect(cn('text-label text-danger-text', 'text-caption')).toBe('text-danger-text text-caption');
  });
});

describe('StickyActionBar (MDV-P1-082)', () => {
  it('is a footer in the flow: status at the start, actions at the end, 56 px reserved, un-pins on a short viewport', async () => {
    const { container } = render(<main><StickyActionBar status={<SaveStatus status={{ state: 'saving' }} />}><button>Review and sign</button></StickyActionBar></main>);
    const bar = container.querySelector('footer')!;
    const cls = bar.className.split(/\s+/);
    expect(cls).toEqual(expect.arrayContaining(['sticky', 'bottom-0', 'min-h-14', '[@media(max-height:480px)]:static']));
    expect(cls).not.toContain('fixed');
    expect(bar.firstElementChild!.textContent).toBe('Saving…');
    expect(bar.lastElementChild!.textContent).toBe('Review and sign');
    expect((await axe.run(container)).violations).toEqual([]);
  });
});
