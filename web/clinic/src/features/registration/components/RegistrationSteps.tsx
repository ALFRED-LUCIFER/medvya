import type { ReactNode } from 'react';
import { BadgeInfo, Info, TriangleAlert } from 'lucide-react';
import { useI18n, type Country } from '@medvya/i18n';
import { Button } from '@medvya/ui';
import type { IdentifierScheme, PayerKind } from '../../patients/domain/directory';
import {
  eidExpired, emirates, genderIdentities, hieFor, indiaStates, languages, nationalities, otherIdChoices, payerFields, payerKinds, payerLabel,
  isSex, quickIdentifier, regionKey, relationships, type RegistrationForm, type Step,
} from '../domain/registration';
import { Checkbox, SelectField, SexField, TextField, type FormProps } from './FormFields';

const grid = 'grid gap-4 md:grid-cols-2';

const isRelationship = (country: Country, r: string): r is (typeof relationships)[Country][number] => (relationships[country] as readonly string[]).includes(r);

function useNames() {
  const { locale } = useI18n();
  const language = new Intl.DisplayNames([locale], { type: 'language' });
  const region = new Intl.DisplayNames([locale], { type: 'region' });
  return { language: (c: string) => language.of(c) ?? c, region: (c: string) => region.of(c) ?? c };
}

function DateOfBirth({ f, country, ageInstead }: { f: FormProps; country: Country; ageInstead: boolean }) {
  const { t } = useI18n();
  return ageInstead
    ? <TextField f={f} name="ageYears" label={t('registration.field.ageYears')} required hint={t('registration.hint.ageYears')} ltr inputMode="numeric" />
    : <TextField f={f} name="dateOfBirth" label={t('registration.field.dateOfBirth')} required ltr type="date"
        hint={country === 'IN' ? t('registration.hint.dateOfBirth') : undefined} />;
}

/** Quick (busy desk): the minimum safe fields on one screen (MDV-P1-015); a minor's guardian too (MDV-P1-020). */
export function QuickFields({ f, country, ageInstead, minor }: { f: FormProps; country: Country; ageInstead: boolean; minor: boolean | null }) {
  const { t } = useI18n();
  const id = quickIdentifier[country];
  return (
    <>
      <div className={grid}>
        <TextField f={f} name="fullName" label={t(country === 'AE' ? 'registration.field.fullName.AE' : 'registration.field.fullName')} required />
        <TextField f={f} name="mobile" label={t('registration.field.mobile')} required hint={t('registration.hint.mobile')} ltr type="tel" inputMode="tel" />
        <DateOfBirth f={f} country={country} ageInstead={ageInstead} />
        <SexField f={f} />
        <TextField f={f} name={id.field} label={t(`registration.field.identifier.${id.scheme}`)} required={id.required}
          hint={t(`registration.hint.identifier.${id.scheme}`)} ltr />
      </div>
      {country === 'IN' && <Checkbox f={f} name="ageInstead" label={t('registration.field.ageInstead')} />}
      {minor && <div className={grid}><GuardianFields f={f} country={country} minor /></div>}
    </>
  );
}

/** The guardian (UAE: guardian or sponsor of a dependant): required while the patient is under 18. Cells of a two-column grid. */
function GuardianFields({ f, country, minor }: { f: FormProps; country: Country; minor: boolean | null }) {
  const { t } = useI18n();
  const hint = t(minor ? 'registration.hint.guardian.minor' : minor === false ? 'registration.hint.guardian.adult' : 'registration.hint.guardian.unknown');
  return (
    <>
      <TextField f={f} name="guardianName" label={t(`registration.field.guardianName.${country}`)} required={minor === true} hint={hint} wide />
      <SelectField f={f} name="guardianRelationship" label={t('registration.field.guardianRelationship')} options={relationships[country]}
        optionLabel={r => t(`registration.relationship.${r}`)} placeholder={t('registration.choose')} required={minor === true} />
      <TextField f={f} name="guardianPhone" label={t('registration.field.guardianPhone')} required={minor === true} ltr type="tel" inputMode="tel" />
    </>
  );
}

/** The emergency contact: a name and number to call, never a portal user. Cells of a two-column grid. */
export function EmergencyContactFields({ f, country, required }: { f: FormProps; country: Country; required: boolean }) {
  const { t } = useI18n();
  return (
    <>
      <TextField f={f} name="ecName" label={t('registration.field.ecName')} required={required} wide />
      <SelectField f={f} name="ecRelationship" label={t('registration.field.ecRelationship')} options={relationships[country]}
        optionLabel={r => t(`registration.relationship.${r}`)} placeholder={t('registration.choose')} required={required} />
      <TextField f={f} name="ecPhone" label={t('registration.field.ecPhone')} required={required} ltr type="tel" inputMode="tel" />
    </>
  );
}

/** Step 3 · Family & emergency (MDV-P1-020): emergency contact (required here), guardian (required under 18). */
export function FamilyStep({ f, country, minor }: { f: FormProps; country: Country; minor: boolean | null }) {
  const { t } = useI18n();
  return (
    <>
      <div className={grid}>
        <EmergencyContactFields f={f} country={country} required />
        <GuardianFields f={f} country={country} minor={minor} />
      </div>
      <p className="flex items-start gap-2 text-sm text-muted-foreground"><Info className="mt-0.5 size-4 shrink-0" aria-hidden />{t('registration.family.note')}</p>
    </>
  );
}

/** Step 1 · Identity & IDs. India: ABHA optional, Aadhaar last 4 only. UAE: Emirates ID or passport, Arabic name entered right to left. */
export function IdentityStep({ f, country, ageInstead, values, today }: { f: FormProps; country: Country; ageInstead: boolean; values: RegistrationForm; today: string }) {
  const { t } = useI18n();
  const names = useNames();
  const choose = t('registration.choose');
  return (
    <>
      <p className="flex items-start gap-2 rounded-md border bg-muted px-3 py-2 text-sm">
        <BadgeInfo className="mt-0.5 size-4 shrink-0" aria-hidden />
        <span><strong>{t(`registration.verify.${country}.title`)}</strong> {t(`registration.verify.${country}.body`)}</span>
      </p>
      {country === 'IN' ? (
        <>
          <div className={grid}>
            <TextField f={f} name="fullName" label={t('registration.field.fullName')} required wide />
            <DateOfBirth f={f} country={country} ageInstead={ageInstead} />
            <SexField f={f} />
            <SelectField f={f} name="genderIdentity" label={t('registration.field.genderIdentity')} options={genderIdentities}
              optionLabel={g => t(`registration.gender.${g}`)} placeholder={choose} hint={t('registration.hint.genderIdentity')} />
            <SelectField f={f} name="preferredLanguage" label={t('registration.field.preferredLanguage')} options={languages.IN} optionLabel={names.language} placeholder={choose} />
            <TextField f={f} name="abha" label={t('registration.field.identifier.Abha')} hint={t('registration.hint.identifier.Abha')} ltr />
            <TextField f={f} name="abhaAddress" label={t('registration.field.abhaAddress')} hint={t('registration.hint.abhaAddress')} ltr />
            <SelectField f={f} name="otherIdType" label={t('registration.field.otherIdType')} options={otherIdChoices}
              optionLabel={o => t(`registration.otherId.${o}`)} placeholder={choose} hint={t('registration.hint.otherIdType')} />
            {(values.otherIdType === 'AadhaarLast4' || values.otherIdType === 'Passport') &&
              <TextField f={f} name="otherIdValue" label={t(`registration.field.otherIdValue.${values.otherIdType}`)} required ltr
                inputMode={values.otherIdType === 'AadhaarLast4' ? 'numeric' : undefined} />}
          </div>
          <Checkbox f={f} name="ageInstead" label={t('registration.field.ageInstead')} />
        </>
      ) : (
        <div className={grid}>
          <TextField f={f} name="fullName" label={t('registration.field.fullName.AE')} required wide />
          <TextField f={f} name="arabicName" label={t('registration.field.arabicName')} dir="rtl" wide hint={t('registration.hint.arabicName')} />
          <DateOfBirth f={f} country={country} ageInstead={false} />
          <SexField f={f} />
          <SelectField f={f} name="nationality" label={t('registration.field.nationality')} options={nationalities} optionLabel={names.region} placeholder={choose} />
          <SelectField f={f} name="preferredLanguage" label={t('registration.field.preferredLanguage')} options={languages.AE} optionLabel={names.language} placeholder={choose} />
          <TextField f={f} name="emiratesId" label={t('registration.field.emiratesId')} hint={t('registration.hint.identifier.EmiratesId')} ltr />
          <div className="flex flex-col gap-1.5">
            <TextField f={f} name="emiratesIdExpiry" label={t('registration.field.emiratesIdExpiry')} ltr type="date" />
            <p aria-live="polite" className="text-sm font-semibold text-warning-700">
              {eidExpired(values.emiratesIdExpiry, today) && <><TriangleAlert className="me-1 inline size-4 align-text-bottom" aria-hidden />{t('registration.warning.eidExpired')}</>}
            </p>
          </div>
          <TextField f={f} name="passport" label={t('registration.field.passport')} hint={t('registration.hint.passport')} ltr />
          <TextField f={f} name="visa" label={t('registration.field.visa')} hint={t('registration.hint.visa')} ltr />
        </div>
      )}
    </>
  );
}

/** Step 2 · Contact & address: phones and email, then the address. */
export function ContactStep({ f, country, values }: { f: FormProps; country: Country; values: RegistrationForm }) {
  const { t } = useI18n();
  return (
    <div className={grid}>
      <TextField f={f} name="mobile" label={t('registration.field.mobile')} required hint={t('registration.hint.mobile')} ltr type="tel" inputMode="tel" />
      <TextField f={f} name="alternatePhone" label={t('registration.field.alternatePhone')} ltr type="tel" inputMode="tel" />
      <TextField f={f} name="email" label={t('registration.field.email')} ltr type="email" inputMode="email" wide />
      <AddressFields f={f} country={country} values={values} />
    </div>
  );
}

/** Indian address with PIN, or UAE address with emirate (which selects the HIE), Makani and P.O. Box. Cells of a two-column grid. */
export function AddressFields({ f, country, values }: { f: FormProps; country: Country; values: RegistrationForm }) {
  const { t } = useI18n();
  const hie = hieFor(values.region);
  return (
    <>
      <TextField f={f} name="line1" label={t(`registration.field.line1.${country}`)} wide hint={t('registration.hint.address')} />
      <TextField f={f} name="line2" label={t(`registration.field.line2.${country}`)} />
      {country === 'IN' ? (
        <>
          <TextField f={f} name="city" label={t('registration.field.city')} />
          <TextField f={f} name="district" label={t('registration.field.district')} />
          <SelectField f={f} name="region" label={t('registration.field.state')} options={indiaStates} optionLabel={s => t(`registration.state.${s}`)} placeholder={t('registration.choose')} />
          <TextField f={f} name="postalCode" label={t('registration.field.pin')} ltr inputMode="numeric" hint={t('registration.hint.pin')} />
        </>
      ) : (
        <>
          <SelectField f={f} name="region" label={t('registration.field.emirate')} options={emirates} optionLabel={e => t(`registration.emirate.${e}`)}
            placeholder={t('registration.choose')} hint={hie ? t('registration.hint.hieSelected', { hie: t(`registration.hie.${hie}`) }) : t('registration.hint.emirate')} />
          <TextField f={f} name="makani" label={t('registration.field.makani')} ltr inputMode="numeric" hint={t('registration.hint.optional')} />
          <TextField f={f} name="poBox" label={t('registration.field.poBox')} ltr inputMode="numeric" hint={t('registration.hint.optional')} />
        </>
      )}
    </>
  );
}

/** Step 4 · Payer: India self-pay / corporate / private insurance (TPA) / government scheme; UAE insurance / self-pay / corporate. */
export function PayerStep({ f, country, values }: { f: FormProps; country: Country; values: RegistrationForm }) {
  const { t } = useI18n();
  const kind = values.payerKind as PayerKind | '';
  return (
    <>
      <div className={grid}>
        <SelectField f={f} name="payerKind" label={t('registration.field.payerKind')} options={payerKinds[country]} optionLabel={k => t(`registration.payerKind.${k}`)}
          placeholder={t('registration.choose')} hint={t('registration.hint.payerKind')} wide />
        {payerFields(country, kind).map(field => (
          <TextField key={field} f={f} name={field} label={t(payerLabel(country, kind, field))} required={field === 'payerName' || (field === 'memberNumber' && kind === 'PrivateInsurance')}
            ltr={field === 'memberNumber' || field === 'validTo'} type={field === 'validTo' ? 'date' : 'text'}
            hint={kind === 'GovernmentScheme' && field === 'payerName' ? t('registration.hint.governmentScheme') : undefined} />
        ))}
      </div>
      {kind === 'PrivateInsurance' && country === 'AE' && <p className="text-sm text-muted-foreground">{t('registration.payer.eligibility')}</p>}
      <p className="flex items-start gap-2 text-sm text-muted-foreground"><Info className="mt-0.5 size-4 shrink-0" aria-hidden />{t('registration.payer.note')}</p>
    </>
  );
}

type ReviewRow = { label: string; parts: ReactNode[]; step: Step };

// Each part is isolated, so Latin numbers and IDs keep their order inside Arabic text.
const Ltr = ({ children }: { children: ReactNode }) => <bdi dir="ltr">{children}</bdi>;

/** Step 6 · Review: what will be saved, each part with Edit. The duplicate decision sits below it. */
export function ReviewStep({ country, values, ageInstead, onEdit }: { country: Country; values: RegistrationForm; ageInstead: boolean; onEdit: (s: Step) => void }) {
  const { t, formatDate } = useI18n();
  const missing = t('patients.record.none');
  const kind = values.payerKind as PayerKind | '';
  const id = (scheme: IdentifierScheme, value: string) => value.trim() ? <>{t(`patients.scheme.${scheme}`)} <Ltr>{value.trim()}</Ltr></> : null;
  const ids = country === 'IN'
    ? [id('Abha', values.abha), id('AbhaAddress', values.abhaAddress),
       values.otherIdType === 'AadhaarLast4' || values.otherIdType === 'Passport' ? id(values.otherIdType, values.otherIdValue) : null]
    : [id('EmiratesId', values.emiratesId), id('Passport', values.passport), id('Visa', values.visa)];
  const region = regionKey(country, values.region);
  const text = (v: string) => (v.trim() ? <bdi>{v.trim()}</bdi> : null);
  const number = (v: string) => (v.trim() ? <Ltr>{v.trim()}</Ltr> : null);
  const contact = (name: string, relationship: string, phone: string) => name.trim()
    ? <><bdi>{name.trim()}</bdi> {isRelationship(country, relationship) && t('registration.review.relationship', { relationship: t(`registration.relationship.${relationship}`) })} {number(phone)}</> : null;
  const born = ageInstead ? t('registration.duplicates.age', { age: values.ageYears })
    : /^\d{4}-\d{2}-\d{2}$/.test(values.dateOfBirth) ? formatDate(new Date(`${values.dateOfBirth}T00:00:00`)) : '';
  const rows: ReviewRow[] = [
    { label: t('registration.review.name'), parts: [text(values.fullName), country === 'AE' ? text(values.arabicName) : null], step: 'identity' },
    { label: t('registration.review.born'), parts: [born], step: 'identity' },
    { label: t('registration.field.sexAtBirth'), parts: [isSex(values.sexAtBirth) ? t(`patients.sex.${values.sexAtBirth}`) : null], step: 'identity' },
    { label: t('registration.review.identifiers'), parts: ids, step: 'identity' },
    { label: t('registration.review.contact'), step: 'contact', parts: [number(values.mobile), number(values.email), text(values.line1), text(values.line2),
      country === 'IN' ? text(values.city) : null, country === 'IN' ? text(values.district) : null, region ? t(region) : null,
      number(country === 'IN' ? values.postalCode : values.makani), country === 'AE' ? number(values.poBox) : null] },
    { label: t('registration.review.family'), step: 'family', parts: [contact(values.ecName, values.ecRelationship, values.ecPhone),
      values.guardianName.trim() ? <>{t('registration.review.guardian')} {contact(values.guardianName, values.guardianRelationship, values.guardianPhone)}</> : null] },
    { label: t('registration.review.payer'), step: 'payer',
      parts: kind ? [t(`registration.payerKind.${kind}`), ...payerFields(country, kind).map(p => (p === 'memberNumber' || p === 'validTo' ? number : text)(values[p]))]
        : [t('patients.record.payerMissing')] },
  ];
  return (
    <dl className="flex flex-col divide-y rounded-lg border">
      {rows.map(r => (
        <div key={r.label} className="flex flex-wrap items-center gap-x-4 gap-y-1 px-3 py-2">
          <dt className="w-36 shrink-0 text-sm font-bold text-muted-foreground">{r.label}</dt>
          <dd className="min-w-0 flex-1 break-words">
            {r.parts.filter(Boolean).length === 0 ? missing
              : r.parts.filter(Boolean).map((part, i) => <span key={i}>{i > 0 && t('registration.review.separator')}{part}</span>)}
          </dd>
          <dd><Button type="button" variant="ghost" onClick={() => onEdit(r.step)} aria-label={t('registration.review.edit', { part: r.label })}>{t('registration.review.editShort')}</Button></dd>
        </div>
      ))}
    </dl>
  );
}
