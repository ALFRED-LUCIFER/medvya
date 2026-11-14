// @vitest-environment jsdom
import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { act, cleanup, render, screen } from '@testing-library/react';
import { Skeleton, SkeletonGroup, skeletonDelayMs } from './index';

afterEach(() => { cleanup(); vi.useRealTimers(); });

describe('Skeleton (MDV-P2-056)', () => {
  it('holds its space but stays invisible for 200 ms, then pulses only where motion is allowed', () => {
    vi.useFakeTimers();
    render(<SkeletonGroup label="Loading Front desk…"><Skeleton className="h-36" /></SkeletonGroup>);
    const block = document.querySelector('[data-slot=skeleton]')!;
    expect(block.className).toContain('invisible');
    expect(block.className).toContain('h-36'); // the final layout's size from the first frame
    act(() => vi.advanceTimersByTime(skeletonDelayMs - 1));
    expect(block.className).toContain('invisible');
    act(() => vi.advanceTimersByTime(1));
    expect(block.className).not.toContain('invisible');
    expect(block.className).toContain('motion-safe:animate-skeleton');
    expect(block.getAttribute('aria-hidden')).toBe('true');
    // The group says Loading once, politely.
    expect(screen.getByRole('status').textContent).toBe('Loading Front desk…');
  });

  it('a load that ends within 200 ms never shows it', () => {
    vi.useFakeTimers();
    const { unmount } = render(<Skeleton />);
    act(() => vi.advanceTimersByTime(skeletonDelayMs - 50));
    expect(document.querySelector('[data-slot=skeleton]')!.className).toContain('invisible');
    unmount(); // the data came: no timer left behind to set state on an unmounted block
    expect(vi.getTimerCount()).toBe(0);
  });
});

describe('Library motion (MDV-P2-056, DESIGN.md Motion)', () => {
  const dir = join(process.cwd(), 'src/components/ui');
  const sources = readdirSync(dir).filter(f => f.endsWith('.tsx')).map(f => [f, readFileSync(join(dir, f), 'utf8')] as const)
    .map(([f, text]) => [f, text.split('\n').filter(l => !/^\s*(?:\/\/|\*|\/\*)/.test(l)).join('\n')] as const); // code, not comments

  it('names its transition properties: no transition-all, and never outline or box-shadow, so a focus ring never fades in', () => {
    expect(sources.length).toBeGreaterThan(10);
    for (const [file, text] of sources) {
      expect(text, file).not.toMatch(/\btransition-all\b|\btransition\b(?![-\w])/);
      // transition-colors and transition-shadow carry outline-color or box-shadow in Tailwind 4.
      expect(text, file).not.toMatch(/\btransition-(?:colors|shadow)\b/);
      for (const [, props] of text.matchAll(/transition-\[([^\]]+)\]/g)) expect(props, file).not.toMatch(/outline|shadow|all/);
    }
  });
});
