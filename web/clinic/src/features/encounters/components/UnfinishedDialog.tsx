import { useRef, type FormEventHandler } from 'react';
import type { UseFormReturn } from 'react-hook-form';
import { useI18n } from '@medvya/i18n';
import { Button, Dialog, DialogContent, DialogDescription, DialogTitle, Field, Input } from '@medvya/ui';
import { authoriseTarget } from '../../prescription/domain/authorisation';

type Props = {
  /** Which act asked: End consultation or Sign note; null: closed. */
  asking: 'end' | 'sign' | null; patientName: string;
  medicines: string[]; unplaced: string[];
  discarding: boolean; form: UseFormReturn<{ reason: string }>; discardPending: boolean; discardFailed: boolean;
  onCancel: () => void; onDiscarding: (open: boolean) => void; onDiscard: FormEventHandler<HTMLFormElement>; onAnyway: () => void;
};

/**
 * MDV-P1-090: before End consultation or Sign note, the unfinished prescription and the orders not placed, named with the patient. Nothing happens
 * until a choice is made: Authorise now (to the prescription, its own act), Discard the draft (with a reason), or End anyway. Escape cancels.
 */
export default function UnfinishedDialog(p: Props) {
  const { t } = useI18n();
  const goTo = useRef<string | null>(null);
  const reasonError = p.form.formState.errors.reason;
  const close = (target: string | null) => { goTo.current = target; p.onCancel(); };
  return (
    <Dialog open={!!p.asking} onOpenChange={open => { if (!open && !p.discardPending) close(null); }}>
      <DialogContent closeLabel={t('finish.cancel')}
        // Opened by state, not a trigger: Authorise now moves focus to the prescription's Authorise button; otherwise Radix returns it.
        onCloseAutoFocus={e => { const target = goTo.current && document.getElementById(goTo.current); goTo.current = null; if (target) { e.preventDefault(); target.focus(); } }}>
        <DialogTitle>{t('finish.title', { name: p.patientName })}</DialogTitle>
        <DialogDescription>{t(p.asking === 'sign' ? 'finish.body.sign' : 'finish.body.end')}</DialogDescription>
        {p.medicines.length > 0 && (
          <section aria-labelledby="finish-rx" className="flex flex-col gap-1">
            <h3 id="finish-rx" className="text-label">{t('finish.rx')}</h3>
            <ul className="list-disc ps-5 text-body">{p.medicines.map((m, i) => <li key={i}>{m}</li>)}</ul>
          </section>
        )}
        {p.unplaced.length > 0 && (
          <section aria-labelledby="finish-orders" className="flex flex-col gap-1">
            <h3 id="finish-orders" className="text-label">{t('finish.orders')}</h3>
            <ul className="list-disc ps-5 text-body">{p.unplaced.map((o, i) => <li key={i}>{o}</li>)}</ul>
          </section>
        )}
        {p.discarding ? (
          <form noValidate onSubmit={p.onDiscard} className="flex flex-col gap-3">
            <Field id="finish-discard-reason" label={t('finish.discardReason')} required error={reasonError && t('finish.discardReasonRequired')}>
              <Input autoComplete="off" autoFocus {...p.form.register('reason')} />
            </Field>
            {p.discardFailed && <p role="alert" className="text-label text-danger-text">{t('finish.discardFailed')}</p>}
            <div className="flex flex-wrap gap-3">
              <Button type="submit" variant="destructive" loading={p.discardPending}>{t('finish.discardConfirm')}</Button>
              <Button type="button" variant="outline" disabled={p.discardPending} onClick={() => p.onDiscarding(false)}>{t('finish.back')}</Button>
            </div>
          </form>
        ) : (
          <div className="flex flex-wrap gap-3">
            {p.medicines.length > 0 && <Button onClick={() => close(authoriseTarget)}>{t('finish.authorise')}</Button>}
            {p.medicines.length > 0 && <Button variant="outline" onClick={() => p.onDiscarding(true)}>{t('finish.discard')}</Button>}
            <Button variant="outline" onClick={p.onAnyway}>{t(p.asking === 'sign' ? 'finish.signAnyway' : 'finish.endAnyway')}</Button>
            <Button variant="ghost" onClick={() => close(null)}>{t('finish.cancel')}</Button>
          </div>
        )}
      </DialogContent>
    </Dialog>
  );
}
