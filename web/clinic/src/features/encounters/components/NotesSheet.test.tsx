// @vitest-environment jsdom
import { useEffect } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { useForm, useWatch } from 'react-hook-form';
import type { NoteForm } from '../domain/note';
import NotesSheet from './NotesSheet';

afterEach(cleanup);

const saved = 'Saved 10:42';

/** The note form as the controller holds it: typing in Notes is a change to `sections.notes`, which queues the autosave. */
function Draft({ onEdit, notes, expose }: { onEdit: (sections: NoteForm['sections']) => void; notes?: string; expose?: (get: () => NoteForm['sections']) => void }) {
  const form = useForm<NoteForm>({ defaultValues: { sections: notes === undefined ? { plan: 'Rest' } : { notes } } });
  useEffect(() => expose?.(() => form.getValues('sections')), [expose, form]);
  const text = useWatch({ control: form.control, name: 'sections.notes' }) ?? '';
  const register = (id: string) => form.register(`sections.${id}`, { onChange: () => onEdit(form.getValues('sections')) });
  return <NotesSheet register={register} readOnly={false} text={text} status={<p role="status">{saved}</p>} />;
}

describe('NotesSheet (MDV-P2-067)', () => {
  it('edits the note form, says notes are written in words, and returns focus to the button', async () => {
    const onEdit = vi.fn();
    render(<Draft onEdit={onEdit} />);
    // The keyboard path: the button has focus when it is pressed.
    const button = screen.getByRole('button', { name: 'Notes' });
    button.focus();
    fireEvent.click(button);
    const sheet = await screen.findByRole('dialog', { name: 'Notes' });
    const box = within(sheet).getByRole('textbox', { name: /Notes/ });
    expect(box.getAttribute('maxlength')).toBe('10000');
    fireEvent.change(box, { target: { value: 'Synthetic: asks about fasting' } });
    expect(onEdit).toHaveBeenLastCalledWith({ plan: 'Rest', notes: 'Synthetic: asks about fasting' });
    expect(within(sheet).getByRole('status').textContent).toBe(saved);

    fireEvent.click(within(sheet).getByRole('button', { name: 'Done' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    const written = screen.getByRole('button', { name: 'Notes · written' });
    await waitFor(() => expect(document.activeElement).toBe(written));
  });

  // The autosave sends every section as text: a field registered while the Sheet is closed would add an undefined `notes` and break every save.
  it('adds nothing to the note until the Sheet is opened, and only text after', async () => {
    let values = (): NoteForm['sections'] => ({});
    render(<Draft onEdit={() => {}} expose={get => { values = get; }} />);
    expect(Object.keys(values())).toEqual(['plan']);
    fireEvent.click(screen.getByRole('button', { name: 'Notes' }));
    fireEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Done' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(Object.values(values()).every(v => typeof v === 'string')).toBe(true);
  });

  it('keeps what was typed when opened again', async () => {
    render(<Draft onEdit={() => {}} notes="Synthetic: recheck feet" />);
    fireEvent.click(screen.getByRole('button', { name: 'Notes · written' }));
    expect((within(await screen.findByRole('dialog')).getByRole('textbox') as HTMLTextAreaElement).value).toBe('Synthetic: recheck feet');
  });

  it('shows a signed note\'s notes with no way to edit them', async () => {
    const register = vi.fn();
    render(<NotesSheet register={register} readOnly text={'Synthetic: line one\nline two'} />);
    fireEvent.click(screen.getByRole('button', { name: 'Notes · written' }));
    const sheet = await screen.findByRole('dialog', { name: 'Notes' });
    expect(within(sheet).queryByRole('textbox')).toBeNull();
    expect(within(sheet).getByText(/line one/).textContent).toBe('Synthetic: line one\nline two');
    expect(within(sheet).getByText('Signed with the note. A change is made as an addendum.')).toBeTruthy();
    expect(register).not.toHaveBeenCalled();
  });
});
