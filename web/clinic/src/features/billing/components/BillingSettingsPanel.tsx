import type { FormEventHandler } from 'react';
import type { FieldError, UseFormReturn } from 'react-hook-form';
import { useI18n, type MessageKey } from '@medvya/i18n';
import { Button, ChoiceRow, Field, Input, StickyActionBar, Textarea } from '@medvya/ui';
import { cardBar, fieldError, fieldGrid } from '../../settings/components/fieldStyles';
import { Section } from '../../settings/components/Section';
import { paymentMethods, type BillingSettingsForm } from '../domain/billingSettings';

type Props = {
  form: UseFormReturn<BillingSettingsForm>; loading: boolean; loadFailed: boolean;
  saving: boolean; saved: boolean; saveFailed: 'changed' | 'failed' | null; onSubmit: FormEventHandler<HTMLFormElement>;
};

/** Settings, Billing (MDV-P4-002): document prefixes, how bills can be paid, discount reasons and the payment link's life. Round-off is the country's rule. */
export default function BillingSettingsPanel(p: Props) {
  const { t } = useI18n();
  if (p.loading) return <p role="status">{t('settings.loading')}</p>;
  if (p.loadFailed) return <p role="alert" className={fieldError}>{t('settings.loadFailed')}</p>;
  const { errors } = p.form.formState;
  const problem = (e: FieldError | undefined) => (e ? t(`billing.error.${e.type === 'server' ? e.message : 'invalid'}` as MessageKey) : null);
  return (
    <Section title={t('billing.settings.title')} intro={t('billing.settings.intro')}>
      <form noValidate onSubmit={p.onSubmit} className="flex flex-col gap-4">
        <div className={fieldGrid}>
          <Field id="billing-invoice-prefix" label={t('billing.settings.invoicePrefix')} hint={t('billing.settings.prefix.hint')} error={problem(errors.invoicePrefix)}>
            <Input dir="ltr" autoComplete="off" {...p.form.register('invoicePrefix')} />
          </Field>
          <Field id="billing-receipt-prefix" label={t('billing.settings.receiptPrefix')} error={problem(errors.receiptPrefix)}>
            <Input dir="ltr" autoComplete="off" {...p.form.register('receiptPrefix')} />
          </Field>
          <Field id="billing-credit-prefix" label={t('billing.settings.creditNotePrefix')} error={problem(errors.creditNotePrefix)}>
            <Input dir="ltr" autoComplete="off" {...p.form.register('creditNotePrefix')} />
          </Field>
        </div>
        <fieldset className="flex flex-col gap-1" aria-describedby={errors.paymentMethods ? 'billing-methods-error' : undefined}>
          <legend className="text-label">{t('billing.settings.methods')}</legend>
          {paymentMethods.map(m => (
            <ChoiceRow key={m} type="checkbox" value={m} {...p.form.register('paymentMethods')}>{t(`billing.method.${m}`)}</ChoiceRow>
          ))}
          {errors.paymentMethods && <p id="billing-methods-error" className={fieldError}>
            {t(errors.paymentMethods.message === 'razorpay_not_connected' ? 'billing.error.razorpay_not_connected' : 'billing.settings.methods.required')}
          </p>}
        </fieldset>
        <Field id="billing-reasons" label={t('billing.settings.reasons')} hint={t('billing.settings.reasons.hint')} error={problem(errors.discountReasons)}>
          <Textarea rows={4} {...p.form.register('discountReasons')} />
        </Field>
        <div className={fieldGrid}>
          <Field id="billing-max-discount" label={t('billing.settings.maxDiscount')} hint={t('billing.settings.maxDiscount.hint')} unit="%" error={problem(errors.maxDiscountPercent)}>
            <Input dir="ltr" inputMode="numeric" {...p.form.register('maxDiscountPercent')} />
          </Field>
          <Field id="billing-link-expiry" label={t('billing.settings.linkExpiry')} hint={t('billing.settings.linkExpiry.hint')} unit={t('billing.field.minutes')} error={problem(errors.paymentLinkExpiryMinutes)}>
            <Input dir="ltr" inputMode="numeric" {...p.form.register('paymentLinkExpiryMinutes')} />
          </Field>
        </div>
        <p className="text-sm text-muted-foreground">{t('billing.settings.roundOff')}</p>
        <StickyActionBar className={cardBar} status={<>
          {p.saved && <p role="status">{t('settings.saved')}</p>}
          {p.saveFailed && <p role="alert" className={fieldError}>{t(p.saveFailed === 'changed' ? 'billing.settings.changed' : 'settings.saveFailed')}</p>}
        </>}>
          <Button type="submit" loading={p.saving}>{t('settings.save')}</Button>
        </StickyActionBar>
      </form>
    </Section>
  );
}
