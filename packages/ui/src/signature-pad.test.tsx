// @vitest-environment jsdom
import { afterEach, describe, expect, it } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import axe from 'axe-core';
import { SignaturePad } from './index';

afterEach(cleanup);

describe('SignaturePad (MDV-P8-019)', () => {
  it('is a named drawing surface with its hint, and Clear waits until something is drawn', async () => {
    const { container } = render(<SignaturePad label="Patient's signature" hint="Sign with a finger or a stylus." clearLabel="Clear" onChange={() => {}} />);
    const pad = screen.getByRole('img', { name: "Patient's signature" });
    expect(pad.getAttribute('aria-describedby')).toBe(screen.getByText('Sign with a finger or a stylus.').id);
    expect(pad.className).toContain('touch-none'); // a stroke never scrolls the page
    expect((screen.getByRole('button', { name: 'Clear' }) as HTMLButtonElement).disabled).toBe(true);
    const result = await axe.run(container, { rules: { 'color-contrast': { enabled: false } } });
    expect(result.violations.map(v => v.id)).toEqual([]);
  });
});
