// @vitest-environment jsdom
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import axe from 'axe-core';
import { Detail, DetailList, LanguageSwitcher } from './index';

afterEach(cleanup);

describe('DetailList', () => {
  it('pairs each label (dt) with its value (dd), with a note and an action as further descriptions', async () => {
    const { container } = render(
      <DetailList layout="rows">
        <Detail label="Date of birth" note="Age 54" action={<button type="button">Edit</button>}>12 Mar 1972</Detail>
        <Detail label="Mobile" valueProps={{ dir: 'ltr' }}>+91 98400 12345</Detail>
      </DetailList>,
    );
    const terms = [...container.querySelectorAll('dl > div > dt')].map(e => e.textContent);
    expect(terms).toEqual(['Date of birth', 'Mobile']);
    expect([...container.querySelectorAll('dl > div:first-child > dd')].map(e => e.textContent)).toEqual(['12 Mar 1972', 'Age 54', 'Edit']);
    expect(screen.getByText('+91 98400 12345').getAttribute('dir')).toBe('ltr');
    expect(container.querySelector('dl')?.getAttribute('data-layout')).toBe('rows');
    expect((await axe.run(container)).violations).toEqual([]);
  });

  it('a class on the value merges with the layout default (print pages set their own sizes)', () => {
    render(<DetailList layout="inline"><Detail label="Token" valueClassName="text-display">A-07</Detail></DetailList>);
    const cls = screen.getByText('A-07').className.split(/\s+/);
    expect(cls).toContain('text-display');
  });
});

describe('LanguageSwitcher', () => {
  it('is a labelled control showing the chosen language', async () => {
    const { container } = render(<LanguageSwitcher value="ar" onChange={() => {}} />);
    expect(screen.getByRole('combobox', { name: 'Language' }).textContent).toBe('العربية');
    expect((await axe.run(container)).violations).toEqual([]);
  });
});
