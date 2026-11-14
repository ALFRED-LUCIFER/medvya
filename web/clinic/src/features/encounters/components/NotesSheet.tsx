import { useState, type ReactNode } from 'react';
import type { UseFormRegisterReturn } from 'react-hook-form';
import { NotebookPen, NotebookText } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, Field, Sheet, SheetContent, SheetTitle, SheetTrigger, Textarea, Tooltip } from '@medvya/ui';
import { isBlank, maxSectionLength, notesSection } from '../domain/note';

/**
 * MDV-P2-067: the visit's free notes, opened from the bottom bar. The text box is the note form's `notes` section, so the autosave and the
 * save state cover it. Signed: the text only, no edit (a change is an addendum). Written or not is said in words on the button.
 * The Sheet returns focus to the button when it closes.
 */
export default function NotesSheet({ register, readOnly, text, status }: {
  register: (sectionId: string) => UseFormRegisterReturn; readOnly: boolean; text: string;
  /** The save state, repeated in the Sheet's footer while it covers the bottom bar. */
  status?: ReactNode;
}) {
  const { t } = useI18n();
  const [open, setOpen] = useState(false);
  const title = t('noteTemplates.section.notes');
  const written = !isBlank(text);
  const label = t(written ? 'studio.notes.written' : 'studio.notes.open');
  const Icon = written ? NotebookText : NotebookPen;
  return (
    <Sheet open={open} onOpenChange={setOpen}>
      {/* Under 1280 px the bar has no room for the words beside Sign and End consultation (the 30 % pinned-chrome rule): the icon, its shape saying
          written or not, with the words for screen readers and in the tip. */}
      <Tooltip content={label}>
        <SheetTrigger asChild>
          <Button variant="outline"><Icon aria-hidden className="size-4" /><span className="max-lg:sr-only">{label}</span></Button>
        </SheetTrigger>
      </Tooltip>
      <SheetContent closeLabel={t('patientBanner.close')} aria-describedby={undefined}>
        <SheetTitle>{title}</SheetTitle>
        {readOnly ? (
          <>
            <p className="text-sm text-muted-foreground">{t('studio.notes.signed')}</p>
            <p className="whitespace-pre-wrap text-base">{text}</p>
          </>
        ) : <NotesBox register={register} label={title} hint={t('studio.notes.hint')} />}
        {/* The Sheet's own footer, pinned at its foot while the text scrolls. */}
        <div className="sticky bottom-0 mt-auto flex flex-wrap items-center justify-between gap-2 border-t bg-card pt-3">
          <div className="min-w-0">{status}</div>
          <Button className="ms-auto" onClick={() => setOpen(false)}>{t('studio.notes.done')}</Button>
        </div>
      </SheetContent>
    </Sheet>
  );
}

/**
 * Its own component so the field registers only once the open Sheet mounts it: a register() call while the Sheet is closed would add an
 * undefined `notes` to every autosave.
 */
function NotesBox({ register, label, hint }: { register: (sectionId: string) => UseFormRegisterReturn; label: string; hint: string }) {
  return (
    <Field id={`note-${notesSection.id}`} label={label} hint={hint}>
      <Textarea className="md:text-base" maxLength={maxSectionLength} {...register(notesSection.id)} />
    </Field>
  );
}
