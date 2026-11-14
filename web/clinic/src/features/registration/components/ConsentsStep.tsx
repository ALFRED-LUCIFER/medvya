import { useI18n, type Country, type MessageKey } from '@medvya/i18n';
import { consentPolicyVersion, hieFor, type FormField, type RegistrationForm } from '../domain/registration';
import { ChoiceRow } from '@medvya/ui';
import type { FormProps } from './FormFields';

type ConsentField = Extract<FormField, `consent${string}`>;

/**
 * One decision: the whole row is the 44px target (like the other checkboxes on this form), and the state is written out so
 * On/Off never depends on colour or position alone.
 */
function Decision({ f, values, name, title, body, locked = false }: {
  f: FormProps; values?: RegistrationForm; name?: ConsentField; title: string; body?: string; locked?: boolean;
}) {
  const { t } = useI18n();
  const id = `consent-${name ?? 'treatment'}`;
  const bound = name ? f.register(name) : { checked: true, readOnly: true };
  const on = locked || (name !== undefined && values?.[name] === true);
  return (
    <ChoiceRow className="items-start gap-3 [&>input]:mt-1.5 [&>input]:size-5" id={id} type="checkbox" role="switch" disabled={locked} aria-labelledby={`${id}-title`} aria-describedby={body ? `${id}-body` : undefined} {...bound}>
      <span className="flex min-w-0 flex-1 flex-col">
        <span id={`${id}-title`} className="font-bold">{title}</span>
        {body && <span id={`${id}-body`} className="text-sm text-muted-foreground">{body}</span>}
      </span>
      <span className="mt-1 text-sm font-bold">{locked ? t('registration.consents.required') : on ? t('registration.consents.on') : t('registration.consents.off')}</span>
    </ChoiceRow>
  );
}

/** The one consent every registration needs, locked on; quick mode shows only this. */
export function TreatmentConsent({ f }: { f: FormProps }) {
  const { t } = useI18n();
  return (
    <div className="rounded-lg border p-3">
      <Decision f={f} locked title={t('registration.consents.treatment.title')} body={t('registration.consents.treatment.body')} />
    </div>
  );
}

/** Step 5 · Consents (MDV-P1-019): separate decisions, each recorded as its own versioned receipt. Recording is asked per visit (P3), not here. */
export default function ConsentsStep({ f, country, values }: { f: FormProps; country: Country; values: RegistrationForm }) {
  const { t } = useI18n();
  const hie = hieFor(values.region);
  const exchangeTitle: MessageKey = country === 'IN' ? 'registration.consents.exchange.IN.title' : 'registration.consents.exchange.AE.pending';
  return (
    <div className="flex flex-col gap-4">
      <p className="rounded-md border border-warning-700 bg-card px-3 py-2 text-sm font-semibold">
        {t('registration.consents.draft')} <bdi dir="ltr">{t('registration.consents.version', { version: consentPolicyVersion })}</bdi>
      </p>
      <TreatmentConsent f={f} />
      <fieldset className="flex flex-col gap-1 rounded-lg border p-3">
        <legend className="px-1 font-bold">{t('registration.consents.reminders.title')}</legend>
        <p className="text-sm text-muted-foreground">{t('registration.consents.reminders.body')}</p>
        <Decision f={f} values={values} name="consentWhatsApp" title={t('registration.consents.channel.whatsapp')} />
        <Decision f={f} values={values} name="consentSms" title={t('registration.consents.channel.sms')} />
        <Decision f={f} values={values} name="consentEmail" title={t('registration.consents.channel.email')} />
      </fieldset>
      <div className="rounded-lg border p-3">
        <Decision f={f} values={values} name="consentExchange"
          title={country === 'AE' && hie ? t('registration.consents.exchange.AE.title', { hie: t(`registration.hie.${hie}`) }) : t(exchangeTitle)}
          body={t(`registration.consents.exchange.${country}.body`)} />
      </div>
      <div className="rounded-lg border p-3">
        <Decision f={f} values={values} name="consentPortal" title={t('registration.consents.portal.title')} body={t('registration.consents.portal.body')} />
      </div>
      <div className="rounded-lg border p-3">
        <Decision f={f} values={values} name="consentMarketing" title={t('registration.consents.marketing.title')} body={t('registration.consents.marketing.body')} />
      </div>
      <p className="text-sm text-muted-foreground">{t('registration.consents.recording')}</p>
    </div>
  );
}
