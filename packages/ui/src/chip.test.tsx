// @vitest-environment jsdom
import { afterEach, describe, expect, it } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import { CircleAlert } from 'lucide-react';
import axe from 'axe-core';
import { Banner, Chip, Tag, toneClasses, type Tone } from './index';

afterEach(cleanup);
const tones = Object.keys(toneClasses) as Tone[];

describe('Chip, Tag and Banner (MDV-P1-083)', () => {
  it('maps each of the seven tones to its own token pair, and only ai is dashed', () => {
    expect(tones).toEqual(['danger', 'warning', 'success', 'info', 'brand', 'ai', 'neutral']);
    for (const tone of tones) {
      expect(toneClasses[tone].split(' ')).toEqual(expect.arrayContaining([`border-${tone}-border`, `bg-${tone}-bg`, `text-${tone}-text`]));
      expect(toneClasses[tone].includes('border-dashed')).toBe(tone === 'ai');
    }
  });

  it('renders the label in words, with or without an icon, and the tone classes', () => {
    const { container } = render(<>
      <Chip tone="danger" label="Critical result" icon={CircleAlert} />
      <Tag tone="neutral" label="From nurse intake" />
      <Banner tone="warning" title="Not checked">The allergy check did not run.</Banner>
    </>);
    const chip = container.querySelector('[data-slot=chip]')!;
    expect(chip.textContent).toBe('Critical result');
    expect(chip.querySelector('svg')!.getAttribute('aria-hidden')).toBe('true');
    expect(chip.className).toContain('bg-danger-bg');
    expect(container.querySelector('[data-slot=tag]')!.textContent).toBe('From nurse intake');
    expect(screen.getByRole('status').textContent).toBe('Not checked' + 'The allergy check did not run.');
  });

  it('refuses a missing label at compile time', () => {
    // @ts-expect-error a Chip needs a label
    render(<Chip tone="info" />);
    // @ts-expect-error a Tag needs a label
    render(<Tag tone="info" icon={CircleAlert} />);
    // @ts-expect-error a Banner needs a title
    render(<Banner tone="info">Text</Banner>);
  });

  it('ai is dashed and always reads the AI draft words; it cannot take a confirmed or signed label', () => {
    // @ts-expect-error the ai tone takes no label of its own
    render(<Chip tone="ai" label="Signed" />);
    render(<Tag tone="ai" />);
    render(<Banner tone="ai" />);
    for (const el of document.querySelectorAll('[data-tone=ai]')) {
      expect(el.textContent).toBe('AI draft · not reviewed');
      expect(el.className).toContain('border-dashed');
    }
    expect(document.querySelectorAll('[data-tone=ai]')).toHaveLength(3);
  });

  it('announces a danger banner as an alert and the others politely; axe finds nothing', async () => {
    const { container } = render(<main>
      <Banner tone="danger" title="Critical result" icon={CircleAlert} action={<button>Open</button>}>Potassium 6.8</Banner>
      <Banner tone="info" title="Booked" />
    </main>);
    expect(screen.getByRole('alert').textContent).toContain('Critical result');
    expect(screen.getByRole('status').textContent).toBe('Booked');
    expect((await axe.run(container)).violations).toEqual([]);
  });
});
