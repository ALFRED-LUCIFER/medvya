import { useEffect, useRef } from 'react';
import type { UseFormRegisterReturn } from 'react-hook-form';
import { useI18n } from '@medvya/i18n';
import { Button, Dialog, DialogContent, DialogDescription, DialogTitle, Input, Label, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Textarea } from '@medvya/ui';
import { maxSectionLength, sectionLabelKey, templateNameKey, type NoteSection, type NoteTemplate, type SavePhase } from '../domain/note';
import { answeredItems, answerLabelKey, answersFor, isStructured, itemLabelKey, needsNote, parseAnswers, toggle, writeAnswers } from '../domain/structured';

// The pieces both consultation views share (the guided steps and the one page, MDV-P2-041), so both edit the same note the same way.

/** One template section: its label, whether it is required, and the text area the form and the autosave own. */
export function SectionField({ section, register, readOnly }: { section: NoteSection; register: (id: string) => UseFormRegisterReturn; readOnly: boolean }) {
  const { t } = useI18n();
  return (
    <div className="flex flex-col gap-1.5">
      <Label htmlFor={`note-${section.id}`}>
        {t(sectionLabelKey(section.id))}
        {section.required && <span className="ms-2 text-sm font-normal text-muted-foreground">{t('noteEditor.required')}</span>}
      </Label>
      <Textarea id={`note-${section.id}`} className="min-h-24" maxLength={maxSectionLength} readOnly={readOnly} aria-required={section.required} {...register(section.id)} />
    </div>
  );
}

export type StructuredProps = { answers: (id: string) => string; onAnswers: (id: string, text: string) => void };

/** A template section of either kind: a text area, or the review of systems / examination by system (MDV-P2-042). */
export function NoteSectionField(p: { section: NoteSection; register: (id: string) => UseFormRegisterReturn; readOnly: boolean } & StructuredProps) {
  return isStructured(p.section)
    ? <StructuredField section={p.section} text={p.answers(p.section.id)} readOnly={p.readOnly} onChange={text => p.onAnswers(p.section.id, text)} />
    : <SectionField section={p.section} register={p.register} readOnly={p.readOnly} />;
}

/**
 * Review of systems: each symptom Yes or No. Examination: each system Normal, Abnormal (with its note) or Not examined. Pressing the chosen
 * answer again clears it; an item not answered stays blank and is not printed. Read-only (signed), the answers in words as the PDF prints them.
 */
export function StructuredField({ section, text, readOnly, onChange, heading }: { section: NoteSection; text: string; readOnly: boolean; onChange: (text: string) => void; heading?: boolean }) {
  const { t } = useI18n();
  // A heading on the print view (its sections are headings); a label-like line in the editor, as a text section's label.
  const Title = heading ? 'h2' : 'p';
  const title = <Title id={`note-${section.id}`} className={heading ? 'font-extrabold' : 'text-sm font-bold'}>{t(sectionLabelKey(section.id))}</Title>;
  if (readOnly) {
    const answered = answeredItems(section, text);
    return (
      <div className="flex flex-col gap-1.5">
        {title}
        {answered.length === 0 ? <p className="text-sm text-muted-foreground">{t('structured.noneAnswered')}</p>
          : <ul className="list-disc ps-5 text-sm">{answered.map(a => <li key={a.id}>{t(a.note ? 'noteTemplates.answerLineNote' : 'noteTemplates.answerLine', { item: t(itemLabelKey(a.id)), answer: t(answerLabelKey(a.answer)), note: a.note ?? '' })}</li>)}</ul>}
      </div>
    );
  }
  const answers = parseAnswers(text);
  const exam = section.kind === 'Examination';
  return (
    <div role="group" aria-labelledby={`note-${section.id}`} className="flex flex-col gap-2">
      {title}
      <p className="text-sm text-muted-foreground">{t(exam ? 'structured.examHint' : 'structured.rosHint')}</p>
      <ul className={exam ? 'flex flex-col gap-3' : 'grid gap-2 sm:grid-cols-2'}>
        {(section.items ?? []).map(item => {
          const a = answers[item];
          const label = `note-${section.id}-${item}`;
          const missing = needsNote(a);
          return (
            <li key={item} className="flex flex-col gap-1.5">
              <div role="group" aria-labelledby={label} className="flex flex-wrap items-center gap-2">
                <span id={label} className="min-w-32 text-sm font-bold">{t(itemLabelKey(item))}</span>
                {answersFor(section).map(answer => (
                  <Button key={answer} type="button" size="sm" variant={a?.answer === answer ? 'selected' : 'outline'} className="min-w-11" aria-pressed={a?.answer === answer}
                    onClick={() => onChange(writeAnswers(section, toggle(answers, item, answer)))}>{t(answerLabelKey(answer))}</Button>
                ))}
              </div>
              {exam && a && (
                <div className="flex flex-col gap-1">
                  <Label htmlFor={`${label}-note`} className="text-sm">{t(a.answer === 'abnormal' ? 'structured.noteRequired' : 'structured.note', { item: t(itemLabelKey(item)) })}</Label>
                  <Input id={`${label}-note`} maxLength={1000} value={a.note ?? ''} aria-required={a.answer === 'abnormal'} aria-invalid={missing}
                    aria-describedby={missing ? `${label}-missing` : undefined}
                    onChange={e => onChange(writeAnswers(section, { ...answers, [item]: { ...a, note: e.target.value } }))} />
                  {missing && <p id={`${label}-missing`} className="text-sm font-bold text-danger-700">{t('structured.noteMissing')}</p>}
                </div>
              )}
            </li>
          );
        })}
      </ul>
    </div>
  );
}

type PickerProps = {
  templateKey: string; choices: NoteTemplate[]; readOnly: boolean;
  pendingSwitch: { template: NoteTemplate; dropped: string[] } | null;
  onRequestSwitch: (key: string) => void; onCancelSwitch: () => void; onConfirmSwitch: () => void;
  /** MDV-P2-042: the note starts on the template suggested for this patient; said in words until the doctor changes it. */
  suggested?: boolean;
};

/** The template control and the warning before a switch drops text that has no place in the new template. */
export function TemplatePicker(p: PickerProps) {
  const { t } = useI18n();
  const templateRef = useRef<HTMLButtonElement>(null);
  return (
    <div className="flex flex-wrap items-center gap-2">
      <Label htmlFor="note-template">{t('noteEditor.template')}</Label>
      <Select value={p.templateKey} onValueChange={p.onRequestSwitch} disabled={p.readOnly}>
        <SelectTrigger ref={templateRef} id="note-template" className="min-w-48"><SelectValue /></SelectTrigger>
        <SelectContent>{p.choices.map(c => <SelectItem key={c.key} value={c.key}>{t(templateNameKey(c.key))}</SelectItem>)}</SelectContent>
      </Select>
      {p.suggested && !p.readOnly && <p className="text-sm text-muted-foreground">{t('structured.suggested')}</p>}
      <Dialog open={p.pendingSwitch !== null} onOpenChange={open => { if (!open) p.onCancelSwitch(); }}>
        <DialogContent closeLabel={t('noteEditor.switch.close')}
          // Nothing opened this dialog by a click on a trigger (the Select that asked is already closed), so give focus back to the Select by hand.
          onCloseAutoFocus={e => { e.preventDefault(); templateRef.current?.focus(); }}>
          <DialogTitle>{t('noteEditor.switch.title')}</DialogTitle>
          {p.pendingSwitch && (
            <DialogDescription>
              {t('noteEditor.switch.body', { template: t(templateNameKey(p.pendingSwitch.template.key)) })}
              {p.pendingSwitch.dropped.length > 0 && ` ${t('noteEditor.switch.dropped', { sections: p.pendingSwitch.dropped.map(id => t(sectionLabelKey(id))).join(', ') })}`}
            </DialogDescription>
          )}
          <div className="flex flex-wrap gap-3">
            <Button onClick={p.onConfirmSwitch}>{t('noteEditor.switch.confirm')}</Button>
            <Button variant="outline" onClick={p.onCancelSwitch}>{t('noteEditor.switch.cancel')}</Button>
          </div>
        </DialogContent>
      </Dialog>
    </div>
  );
}

/**
 * Never colour alone: each state has words, and "Saved" appears only once the server has answered, with the time (MDV-P1-089). One status
 * element carries saving, saved and not saved, so each change is read out politely. A conflict is an alert; the text stays on screen either way.
 */
export function SaveState({ phase, savedAt, reloading, onRetry, onReapply }: { phase: SavePhase; savedAt: string | null; reloading: boolean; onRetry: () => void; onReapply: () => void }) {
  const { t } = useI18n();
  if (phase === 'conflict') return (
    <div role="alert" className="flex flex-wrap items-center gap-3 font-bold text-danger-700">
      <span>{t('noteEditor.save.conflict')}</span>
      <Button variant="outline" loading={reloading} onClick={onReapply}>{t('noteEditor.save.reapply')}</Button>
    </div>
  );
  return (
    <div className="flex flex-wrap items-center gap-3">
      <p role="status" className={phase === 'failed' ? 'font-bold text-danger-700' : 'text-sm text-muted-foreground'}>
        {phase === 'saved' ? t('noteEditor.save.saved', { time: savedAt ?? '' }) : t(`noteEditor.save.${phase}`)}
      </p>
      {phase === 'failed' && <Button variant="outline" onClick={onRetry}>{t('noteEditor.save.retry')}</Button>}
    </div>
  );
}

/**
 * SIGNED v1 · read-only. Signing removes the button the doctor pressed, so focus moves here (read out as it arrives) instead of falling to the page;
 * a note that was already signed when opened leaves focus alone.
 */
export function SignedBanner({ signed, versionNo }: { signed: boolean; versionNo: number }) {
  const { t } = useI18n();
  const ref = useRef<HTMLParagraphElement>(null);
  const wasDraft = useRef(false);
  useEffect(() => {
    if (signed && wasDraft.current) ref.current?.focus();
    wasDraft.current = !signed;
  }, [signed]);
  return signed ? <p ref={ref} tabIndex={-1} role="status" className="font-bold focus:outline-none">{t('noteEditor.signed', { version: String(versionNo) })}</p> : null;
}
