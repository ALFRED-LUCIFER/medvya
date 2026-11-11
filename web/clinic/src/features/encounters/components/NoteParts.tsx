import { useEffect, useRef } from 'react';
import type { UseFormRegisterReturn } from 'react-hook-form';
import { useI18n } from '@medvya/i18n';
import { Button, Dialog, DialogContent, DialogDescription, DialogTitle, Label, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Textarea } from '@medvya/ui';
import { maxSectionLength, sectionLabelKey, templateNameKey, type NoteSection, type NoteTemplate, type SavePhase } from '../domain/note';

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

type PickerProps = {
  templateKey: string; choices: NoteTemplate[]; readOnly: boolean;
  pendingSwitch: { template: NoteTemplate; dropped: string[] } | null;
  onRequestSwitch: (key: string) => void; onCancelSwitch: () => void; onConfirmSwitch: () => void;
};

/** The template control and the warning before a switch drops text that has no place in the new template. */
export function TemplatePicker(p: PickerProps) {
  const { t } = useI18n();
  const templateRef = useRef<HTMLButtonElement>(null);
  return (
    <div className="flex items-center gap-2">
      <Label htmlFor="note-template">{t('noteEditor.template')}</Label>
      <Select value={p.templateKey} onValueChange={p.onRequestSwitch} disabled={p.readOnly}>
        <SelectTrigger ref={templateRef} id="note-template" className="min-h-11 min-w-48"><SelectValue /></SelectTrigger>
        <SelectContent>{p.choices.map(c => <SelectItem key={c.key} value={c.key}>{t(templateNameKey(c.key))}</SelectItem>)}</SelectContent>
      </Select>
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
            <Button className="min-h-11" onClick={p.onConfirmSwitch}>{t('noteEditor.switch.confirm')}</Button>
            <Button variant="outline" className="min-h-11" onClick={p.onCancelSwitch}>{t('noteEditor.switch.cancel')}</Button>
          </div>
        </DialogContent>
      </Dialog>
    </div>
  );
}

/** Never colour alone: each state has words. A failure or conflict is an alert; the text stays on screen either way. */
export function SaveState({ phase, reloading, onRetry, onReapply }: { phase: SavePhase; reloading: boolean; onRetry: () => void; onReapply: () => void }) {
  const { t } = useI18n();
  if (phase === 'failed') return (
    <div role="alert" className="flex flex-wrap items-center gap-3 font-bold text-danger-700">
      <span>{t('noteEditor.save.failed')}</span>
      <Button variant="outline" className="min-h-11" onClick={onRetry}>{t('noteEditor.save.retry')}</Button>
    </div>
  );
  if (phase === 'conflict') return (
    <div role="alert" className="flex flex-wrap items-center gap-3 font-bold text-danger-700">
      <span>{t('noteEditor.save.conflict')}</span>
      <Button variant="outline" className="min-h-11" loading={reloading} onClick={onReapply}>{t('noteEditor.save.reapply')}</Button>
    </div>
  );
  return <p role="status" className="text-sm text-muted-foreground">{t(`noteEditor.save.${phase}`)}</p>;
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
