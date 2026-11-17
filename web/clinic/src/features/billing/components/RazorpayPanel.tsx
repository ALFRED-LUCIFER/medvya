import type { FormEventHandler } from 'react';
import type { FieldError, UseFormReturn } from 'react-hook-form';
import { useI18n, type MessageKey } from '@medvya/i18n';
import { Button, Card, Dialog, DialogContent, DialogDescription, DialogTitle, Field, Input } from '@medvya/ui';
import { fieldError } from '../../settings/components/fieldStyles';
import { razorpayEvents, type RazorpayForm, type RazorpayStatus } from '../domain/razorpay';

type Props = {
  status: RazorpayStatus | null; loading: boolean; loadFailed: boolean; webhookUrl: string | null; copied: boolean; onCopy: () => void;
  form: UseFormReturn<RazorpayForm>; open: boolean; saving: boolean; saveFailed: boolean; onOpen: () => void; onClose: () => void; onSubmit: FormEventHandler<HTMLFormElement>;
};

/**
 * Settings, Billing, Razorpay (MDV-P4-007, ADR-023): the clinic's own account. Keys are typed once in a dialog and never shown again; the card shows the
 * mode, the key's last four and the webhook URL with the events to turn on in the Razorpay dashboard.
 */
export default function RazorpayPanel(p: Props) {
  const { t } = useI18n();
  if (p.loading) return <p role="status">{t('settings.loading')}</p>;
  if (p.loadFailed || !p.status) return <p role="alert" className={fieldError}>{t('settings.loadFailed')}</p>;
  const s = p.status;
  const { errors } = p.form.formState;
  // A server refusal names its code; a refusal in the browser gets the field's own message.
  const problem = (e: FieldError | undefined, own: MessageKey) => (e ? t(e.type === 'server' ? (`billing.error.${e.message}` as MessageKey) : own) : null);
  return (
    <Card title={t('billing.razorpay.title')} intro={t('billing.razorpay.intro')}
      actions={<Button variant="outline" onClick={p.onOpen}>{t(s.connected ? 'billing.razorpay.replace' : 'billing.razorpay.connect')}</Button>}>
      {s.connected ? (
        <div className="flex flex-col gap-3">
          <p className="font-bold">{t('billing.razorpay.connected', { mode: t(`billing.razorpay.mode.${s.mode ?? 'Test'}`), ending: s.keyIdEnding ?? '' })}</p>
          {p.webhookUrl && (
            <div className="flex flex-col gap-1.5">
              <p className="text-label">{t('billing.razorpay.webhookUrl')}</p>
              <div className="flex flex-wrap items-center gap-2">
                <code dir="ltr" className="min-w-0 break-all rounded-md bg-muted px-2 py-1 text-sm">{p.webhookUrl}</code>
                <Button variant="outline" size="sm" onClick={p.onCopy}>{t('billing.razorpay.copy')}</Button>
                {p.copied && <span role="status" className="text-sm">{t('billing.razorpay.copied')}</span>}
              </div>
              <p className="text-sm text-muted-foreground">{t('billing.razorpay.events')}</p>
              <ul className="flex flex-wrap gap-2">
                {razorpayEvents.map(e => <li key={e}><code dir="ltr" className="rounded-md bg-muted px-2 py-1 text-sm">{e}</code></li>)}
              </ul>
            </div>
          )}
        </div>
      ) : <p>{t('billing.razorpay.notConnected')}</p>}
      <Dialog open={p.open} onOpenChange={open => { if (!open) p.onClose(); }}>
        <DialogContent closeLabel={t('billing.razorpay.cancel')}>
          <DialogTitle>{t('billing.razorpay.dialogTitle')}</DialogTitle>
          <DialogDescription>{t('billing.razorpay.dialogIntro')}</DialogDescription>
          <form noValidate onSubmit={p.onSubmit} className="flex flex-col gap-3">
            <Field id="razorpay-key-id" label={t('billing.razorpay.keyId')} hint={t('billing.razorpay.keyId.hint')} error={problem(errors.keyId, 'billing.razorpay.keyId.format')}>
              <Input dir="ltr" autoComplete="off" spellCheck={false} {...p.form.register('keyId')} />
            </Field>
            <Field id="razorpay-key-secret" label={t('billing.razorpay.keySecret')} error={problem(errors.keySecret, 'billing.error.required')}>
              <Input dir="ltr" type="password" autoComplete="new-password" {...p.form.register('keySecret')} />
            </Field>
            <Field id="razorpay-webhook-secret" label={t('billing.razorpay.webhookSecret')} hint={t('billing.razorpay.webhookSecret.hint')} error={problem(errors.webhookSecret, 'billing.error.required')}>
              <Input dir="ltr" type="password" autoComplete="new-password" {...p.form.register('webhookSecret')} />
            </Field>
            {p.saveFailed && <p role="alert" className={fieldError}>{t('settings.saveFailed')}</p>}
            <div className="flex flex-wrap gap-2">
              <Button type="submit" loading={p.saving}>{t('billing.razorpay.save')}</Button>
              <Button type="button" variant="outline" disabled={p.saving} onClick={p.onClose}>{t('billing.razorpay.cancel')}</Button>
            </div>
          </form>
        </DialogContent>
      </Dialog>
    </Card>
  );
}
