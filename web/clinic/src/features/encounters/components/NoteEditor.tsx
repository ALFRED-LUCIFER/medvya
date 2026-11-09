import type { UseFormRegisterReturn } from 'react-hook-form';
import { useI18n } from '@medvya/i18n';
import { Button, Dialog, DialogContent, DialogDescription, DialogTitle, Label, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Textarea } from '@medvya/ui';
import { maxSectionLength, sectionLabelKey, templateNameKey, type NoteSignature, type NoteTemplate, type SavePhase } from '../domain/note';
import { SignedLine, SignNote, type SignProblem } from './NoteSign';

type Props = {
  register: (sectionId: string) => UseFormRegisterReturn;
  loading: boolean; forbidden: boolean; loadFailed: boolean;
  template: NoteTemplate | null; templateKey: string | null; choices: NoteTemplate[];
  readOnly: boolean; phase: SavePhase; reloading: boolean;
  pendingSwitch: { template: NoteTemplate; dropped: string[] } | null;
  onRequestSwitch: (key: string) => void; onCancelSwitch: () => void; onConfirmSwitch: () => void;
  onRetry: () => void; onReapply: () => void;
  canSign: boolean; signing: boolean; confirming: boolean; signProblem: SignProblem; signature: NoteSignature | null;
  onRequestSign: () => void; onCancelSign: () => void; onConfirmSign: () => void;
  /** Shown beside the signature: the signer's id is all the server sends, so the name is only known for the person looking. */
  userId: string; userName: string; timeZone: string | undefined;
};

/** E-04: the consultation note. Sections come from the template; saving is automatic and its state is always shown. Read-only once not a draft. */
export default function NoteEditor(p: Props) {
  const { t } = useI18n();
  if (p.loading) return <p role="status">{t('noteEditor.loading')}</p>;
  if (p.loadFailed || !p.template || !p.templateKey) return <p role="alert" className="font-bold text-danger-700">{t(p.forbidden ? 'noteEditor.forbidden' : 'noteEditor.loadFailed')}</p>;
  return (
    <section aria-labelledby="note-title" className="flex flex-col gap-4 rounded-xl border bg-card p-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 id="note-title" className="text-lg font-extrabold">{t('noteEditor.title')}</h2>
        <div className="flex items-center gap-2">
          <Label htmlFor="note-template">{t('noteEditor.template')}</Label>
          <Select value={p.templateKey} onValueChange={p.onRequestSwitch} disabled={p.readOnly}>
            <SelectTrigger id="note-template" className="min-h-11 min-w-48"><SelectValue /></SelectTrigger>
            <SelectContent>{p.choices.map(c => <SelectItem key={c.key} value={c.key}>{t(templateNameKey(c.key))}</SelectItem>)}</SelectContent>
          </Select>
        </div>
      </div>
      <p className="text-sm text-muted-foreground">{t('noteEditor.manualBanner')}</p>
      {p.readOnly && <p role="status" className="font-bold">{t('noteEditor.signed')}</p>}
      {p.signature && <SignedLine signature={p.signature} userId={p.userId} userName={p.userName} timeZone={p.timeZone} />}
      <SaveState phase={p.phase} reloading={p.reloading} onRetry={p.onRetry} onReapply={p.onReapply} />
      <div className="flex flex-col gap-4">
        {p.template.sections.map(s => (
          <div key={s.id} className="flex flex-col gap-1.5">
            <Label htmlFor={`note-${s.id}`}>
              {t(sectionLabelKey(s.id))}
              {s.required && <span className="ms-2 text-sm font-normal text-muted-foreground">{t('noteEditor.required')}</span>}
            </Label>
            <Textarea id={`note-${s.id}`} className="min-h-24" maxLength={maxSectionLength} readOnly={p.readOnly} aria-required={s.required} {...p.register(s.id)} />
          </div>
        ))}
      </div>
      {!p.readOnly && (
        <>
          {!p.canSign && <p className="text-sm text-muted-foreground">{t('noteSign.onlyDoctor')}</p>}
          <SignNote canSign={p.canSign} signing={p.signing} confirming={p.confirming} problem={p.signProblem}
            onRequest={p.onRequestSign} onCancel={p.onCancelSign} onConfirm={p.onConfirmSign} />
        </>
      )}
      <Dialog open={p.pendingSwitch !== null} onOpenChange={open => { if (!open) p.onCancelSwitch(); }}>
        <DialogContent closeLabel={t('noteEditor.switch.close')}>
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
    </section>
  );
}

/** Never colour alone: each state has words. A failure or conflict is an alert; the text stays on screen either way. */
function SaveState({ phase, reloading, onRetry, onReapply }: { phase: SavePhase; reloading: boolean; onRetry: () => void; onReapply: () => void }) {
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
