import type { FormEventHandler } from 'react';
import type { UseFormRegisterReturn } from 'react-hook-form';
import { useI18n } from '@medvya/i18n';
import { Button, Dialog, DialogContent, DialogDescription, DialogTitle, Field, Textarea } from '@medvya/ui';
import { maxReasonLength } from '../domain/note';

export type TakeOverProps = {
  open: boolean; busy: boolean; failed: boolean; reasonMissing: boolean;
  register: UseFormRegisterReturn; onOpenChange: (open: boolean) => void; onSubmit: FormEventHandler<HTMLFormElement>;
};

/**
 * #1365: shown to a doctor who does not lead an unsigned visit. Says in words why Sign is locked, and offers to take the visit over with a
 * reason; the server records both doctors and the reason.
 */
export default function TakeOverVisit(p: TakeOverProps) {
  const { t } = useI18n();
  return (
    <>
      <p id="sign-remaining" className="font-bold">{t('takeOver.why')}</p>
      <Button type="button" variant="outline" onClick={() => p.onOpenChange(true)}>{t('takeOver.button')}</Button>
      <Dialog open={p.open} onOpenChange={p.onOpenChange}>
        <DialogContent closeLabel={t('noteSign.confirm.close')}>
          <DialogTitle>{t('takeOver.title')}</DialogTitle>
          <DialogDescription>{t('takeOver.body')}</DialogDescription>
          <form noValidate className="flex flex-col gap-3" onSubmit={p.onSubmit}>
            <Field id="take-over-reason" label={t('takeOver.reason')} hint={t('takeOver.reasonHint')} error={p.reasonMissing ? t('takeOver.reasonRequired') : undefined}>
              <Textarea maxLength={maxReasonLength} aria-required rows={3} {...p.register} />
            </Field>
            {p.failed && <p role="alert" className="font-bold text-danger-700">{t('takeOver.failed')}</p>}
            <div className="flex flex-wrap gap-3">
              <Button type="submit" loading={p.busy}>{t('takeOver.confirm')}</Button>
              <Button type="button" variant="outline" disabled={p.busy} onClick={() => p.onOpenChange(false)}>{t('takeOver.cancel')}</Button>
            </div>
          </form>
        </DialogContent>
      </Dialog>
    </>
  );
}
