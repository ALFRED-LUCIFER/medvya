// @vitest-environment jsdom
import { useState } from 'react';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import axe from 'axe-core';
import { ErrorSummary, Field, FieldMarks, Input } from './index';

// MDV-P1-085: Field and ErrorSummary. Playwright drives the real submit and focus on /__ui.
afterEach(cleanup);

describe('Field', () => {
  it('links the hint and error to the control, marks it invalid, and shows the error as text with an icon', async () => {
    const { container } = render(<Field id="pin" label="PIN code" hint="6 digits" error="Enter a 6-digit PIN code" unit="kg"><Input /></Field>);
    const input = screen.getByRole('textbox', { name: 'PIN code' });
    expect(input.id).toBe('pin');
    expect(input.getAttribute('aria-invalid')).toBe('true');
    expect(input.getAttribute('aria-describedby')).toBe('pin-hint pin-error');
    expect(document.getElementById('pin-error')!.textContent).toBe('Enter a 6-digit PIN code');
    expect(document.getElementById('pin-error')!.querySelector('svg[aria-hidden]')).not.toBeNull();
    expect(screen.getByText('kg')).toBeTruthy();
    expect((await axe.run(container)).violations).toEqual([]);
  });

  it('has no error wiring when valid', () => {
    render(<Field id="name" label="Full name"><Input /></Field>);
    const input = screen.getByRole('textbox');
    expect(input.getAttribute('aria-invalid')).toBeNull();
    expect(input.getAttribute('aria-describedby')).toBeNull();
  });

  it('marks required fields in words in a required-mark form, never an asterisk alone', () => {
    render(<FieldMarks mark="required"><Field id="a" label="Full name" required><Input /></Field><Field id="b" label="Email"><Input /></Field></FieldMarks>);
    expect(screen.getByLabelText('Full name (required)').getAttribute('aria-required')).toBe('true');
    expect(screen.getByLabelText('Email')).toBeTruthy();
    expect(document.body.textContent).not.toContain('*');
  });

  it('marks optional fields in words in an optional-mark form', () => {
    render(<FieldMarks mark="optional"><Field id="a" label="Full name" required><Input /></Field><Field id="b" label="Email"><Input /></Field></FieldMarks>);
    expect(screen.getByLabelText('Full name')).toBeTruthy();
    expect(screen.getByLabelText('Email (optional)')).toBeTruthy();
  });
});

function Form() {
  const [submits, setSubmits] = useState(0);
  const [name, setName] = useState('');
  const errors = submits && !name ? [{ id: 'n', message: 'Enter the full name' }, { id: 'm', message: 'Enter a mobile number' }] : [];
  return (
    <form noValidate onSubmit={e => { e.preventDefault(); setSubmits(s => s + 1); }}>
      <ErrorSummary title="There is a problem" errors={errors} focusKey={submits} />
      <Field id="n" label="Full name"><Input value={name} onChange={e => setName(e.target.value)} /></Field>
      <Field id="m" label="Mobile"><Input /></Field>
      <button>Save</button>
    </form>
  );
}

describe('ErrorSummary', () => {
  it('renders nothing without errors; after a failed submit it lists links, takes focus, and a link focuses its field', async () => {
    const { container } = render(<Form />);
    expect(container.querySelector('[data-slot=error-summary]')).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    const summary = screen.getByRole('group', { name: 'There is a problem' });
    expect(document.activeElement).toBe(summary);
    const links = screen.getAllByRole('link');
    expect(links.map(l => l.textContent)).toEqual(['Enter the full name', 'Enter a mobile number']);
    fireEvent.click(links[1]);
    expect(document.activeElement).toBe(document.getElementById('m'));
    expect(summary.getAttribute('aria-live')).toBeNull(); // announced by focus once, not by a live region on every change
    expect((await axe.run(container)).violations).toEqual([]);
  });

  it('does not take focus back while the user types, only on the next submit', () => {
    render(<Form />);
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    const name = document.getElementById('n')!;
    name.focus();
    fireEvent.change(name, { target: { value: 'D' } }); // the errors clear and the summary goes
    fireEvent.change(name, { target: { value: '' } }); // they come back without a submit: focus must stay in the field
    expect(screen.getByRole('group', { name: 'There is a problem' })).toBeTruthy();
    expect(document.activeElement).toBe(name);
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    expect(document.activeElement).toBe(screen.getByRole('group', { name: 'There is a problem' }));
  });
});
