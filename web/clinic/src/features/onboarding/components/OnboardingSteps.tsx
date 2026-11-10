import type { ReactNode } from 'react';
import { useWatch } from 'react-hook-form';
import {
  BadgeCheck, Building2, ClipboardList, CreditCard, FileSpreadsheet, HeartPulse, Info, MessageCircle, QrCode, ShieldCheck, Stethoscope,
  Trash2, UserPlus, UserRound, ArrowLeftRight, Headset, type LucideIcon,
} from 'lucide-react';
import { useI18n, type Country, type MessageKey } from '@medvya/i18n';
import { Button, Input } from '@medvya/ui';
import { connections, patientStarts, specialtyIdOf, weekDays, type Rules } from '../domain/onboarding';
import { Field, FieldError, SelectField, TextField, type FormProps } from './Fields';

const grid = 'grid gap-4 md:grid-cols-2';
// A choice drawn as a card or chip: the native input stays (keyboard, screen reader), the label carries the look and the focus ring.
const card = 'flex min-h-11 cursor-pointer gap-3 rounded-xl border bg-card p-4 text-sm has-[:checked]:border-2 has-[:checked]:border-brand-700 has-[:checked]:bg-brand-50 has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-ring';
const chip = 'inline-flex min-h-11 cursor-pointer items-center gap-2 rounded-full border bg-card px-4 text-sm font-semibold has-[:checked]:border-brand-700 has-[:checked]:bg-brand-50 has-[:checked]:text-brand-800 has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-ring';

type StepProps = FormProps & { country: Country; rules: Rules };

function Group({ legend, children, error }: { legend: string; children: ReactNode; error?: { id: string; code: string | undefined } }) {
  return (
    <fieldset className="flex flex-col gap-3" aria-describedby={error?.code ? error.id : undefined}>
      <legend className="mb-1 text-sm font-bold">{legend}</legend>
      {children}
      {error && <FieldError id={error.id} code={error.code} />}
    </fieldset>
  );
}

function Note({ icon: Icon = Info, children }: { icon?: LucideIcon; children: ReactNode }) {
  return <p role="note" className="flex items-start gap-2 rounded-lg bg-muted p-3 text-sm"><Icon className="mt-0.5 size-4 shrink-0" aria-hidden />{children}</p>;
}

/** Step 1: kind of practice, who you are, the practice, specialties and (for a doctor) the registration by country rules. */
export function PracticeStep({ form, country, rules }: StepProps) {
  const { t } = useI18n();
  const mode = useWatch({ control: form.control, name: 'mode' });
  const errors = form.formState.errors;
  return (
    <div className="flex flex-col gap-6">
      <Group legend={t('onboarding.mode.legend')}>
        <div className={grid}>
          {(['Solo', 'Clinic'] as const).map(m => (
            <label key={m} className={card}>
              <input type="radio" value={m} {...form.register('mode')} className="mt-0.5 size-4 accent-brand-700" />
              <span className="flex flex-col gap-0.5">
                <strong className="flex items-center gap-1.5">
                  {m === 'Solo' ? <Stethoscope className="size-4" aria-hidden /> : <Building2 className="size-4" aria-hidden />}{t(`onboarding.mode.${m}`)}
                </strong>
                <span className="text-muted-foreground">{t(`onboarding.mode.${m}.hint`)}</span>
              </span>
            </label>
          ))}
        </div>
      </Group>
      <div className={grid}>
        <TextField form={form} name="fullName" label={t('onboarding.field.fullName')} required />
        <TextField form={form} name="clinicName" label={t('onboarding.field.clinicName')} required />
        <TextField form={form} name="address" label={t('onboarding.field.address')} required wide />
      </div>
      <Group legend={t('onboarding.specialties.legend')} error={{ id: 'specialties-error', code: errors.specialties?.message }}>
        <div className="flex flex-wrap gap-2">
          {rules.specialties.map(s => specialtyIdOf(s.code)).filter(id => id !== undefined).map(id => (
            <label key={id} className={chip}>
              <input type="checkbox" value={id} {...form.register('specialties')} className="size-4 accent-brand-700" />{t(`onboarding.specialty.${id}`)}
            </label>
          ))}
        </div>
      </Group>
      {mode === 'Solo' && (
        <div className="flex flex-col gap-3">
          <div className={grid}>
            <SelectField form={form} name="authority" label={t(`onboarding.field.authority.${country}`)} required placeholder={t('onboarding.choose')}
              options={rules.authorities.map(a => a.code)} optionLabel={code => `${code} · ${rules.authorities.find(a => a.code === code)?.name ?? ''}`} />
            <TextField form={form} name="number" label={t(`onboarding.field.licenceNumber.${country}`)} required ltr hint={t(`onboarding.hint.licenceNumber.${country}`)} />
          </div>
          <Group legend={t('onboarding.attest.legend')} error={{ id: 'attested-error', code: errors.attested?.message }}>
            <label className={chip}>
              <input type="checkbox" {...form.register('attested')} className="size-4 accent-brand-700" />{t('onboarding.attest.label')}
            </label>
          </Group>
          <Note>{t('onboarding.attest.notice')}</Note>
        </div>
      )}
      <details className="rounded-xl border p-4" open={Boolean(errors.taxId || errors.registrationNumber || errors.qualifications || errors.languages)}>
        <summary className="flex min-h-11 cursor-pointer items-center text-sm font-bold">{t('onboarding.more.legend')}</summary>
        <div className={`${grid} pt-3`}>
          {mode === 'Solo' && <TextField form={form} name="qualifications" label={t('onboarding.field.qualifications')} optional />}
          {mode === 'Solo' && <TextField form={form} name="languages" label={t('onboarding.field.languages')} optional hint={t('onboarding.hint.languages')} />}
          <TextField form={form} name="registrationNumber" label={t(`onboarding.field.registration.${country}`)} optional ltr />
          <TextField form={form} name="taxId" label={t(`onboarding.field.taxId.${country}`)} optional ltr hint={t(`onboarding.hint.taxId.${country}`)} />
        </div>
      </details>
    </div>
  );
}

function TimeRange({ form, from, to, label }: FormProps & { from: 'weekdayStart1' | 'weekdayStart2' | 'saturdayStart'; to: 'weekdayEnd1' | 'weekdayEnd2' | 'saturdayEnd'; label: string }) {
  const { t } = useI18n();
  return (
    <fieldset className="flex flex-col gap-1.5">
      <legend className="text-sm font-semibold">{label}</legend>
      <div className="grid grid-cols-2 gap-3">
        <TextField form={form} name={from} label={t('onboarding.field.from')} type="time" ltr />
        <TextField form={form} name={to} label={t('onboarding.field.to')} type="time" ltr />
      </div>
    </fieldset>
  );
}

/** Step 2: open days, the usual hours (Saturday may differ), appointment length, and the two fees in whole units (minor units on the wire). */
export function HoursStep({ form, rules }: StepProps) {
  const { t } = useI18n();
  const [days, slot, w1s, w1e, w2s, w2e, ss, se] = useWatch({ control: form.control, name: ['days', 'slotMinutes', 'weekdayStart1', 'weekdayEnd1', 'weekdayStart2', 'weekdayEnd2', 'saturdayStart', 'saturdayEnd'] });
  const errors = form.formState.errors;
  const span = (a: string, b: string) => (a && b ? `${a}–${b}` : '');
  const usual = [span(w1s, w1e), span(w2s, w2e)].filter(Boolean).join(' · ');
  const saturday = span(ss, se) || usual;
  const windowError = errors.weekdayStart1?.message ?? errors.saturdayStart?.message;
  return (
    <div className="flex flex-col gap-6">
      <Group legend={t('onboarding.days.legend')} error={{ id: 'days-error', code: errors.days?.message }}>
        <div className="flex flex-col divide-y rounded-xl border">
          {weekDays.map(d => {
            const open = days.includes(d);
            return (
              <label key={d} className="flex min-h-12 cursor-pointer items-center gap-3 px-4 has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-ring">
                <input type="checkbox" role="switch" value={d} {...form.register('days')} className="size-5 accent-brand-700" />
                <strong className="w-28 text-sm">{t(`onboarding.day.${d}`)}</strong>
                <span dir="ltr" className={`text-sm ${open ? '' : 'text-muted-foreground'}`}>{open ? (d === 'Saturday' ? saturday : usual) : t('onboarding.closed')}</span>
              </label>
            );
          })}
        </div>
      </Group>
      <div className={grid}>
        <TimeRange form={form} from="weekdayStart1" to="weekdayEnd1" label={t('onboarding.hours.morning')} />
        <TimeRange form={form} from="weekdayStart2" to="weekdayEnd2" label={t('onboarding.hours.evening')} />
        {days.includes('Saturday') && <TimeRange form={form} from="saturdayStart" to="saturdayEnd" label={t('onboarding.hours.saturdayOwn')} />}
      </div>
      {/* Window errors belong to a pair of fields, so they are shown once under the hours. */}
      {windowError && <FieldError id="hours-window-error" code={windowError} />}
      <Group legend={t('onboarding.field.slot')} error={{ id: 'slot-error', code: errors.slotMinutes?.message }}>
        <div className="flex flex-wrap gap-2">
          {rules.slotMinutes.map(String).map(m => (
            <label key={m} className={chip}>
              <input type="radio" value={m} {...form.register('slotMinutes')} className="size-4 accent-brand-700" />{t('onboarding.slotOption', { n: m })}
            </label>
          ))}
        </div>
      </Group>
      <Group legend={t('onboarding.services.legend')}>
        <div className={grid}>
          <TextField form={form} name="newVisitFee" label={t('onboarding.field.newFee', { currency: rules.currency })} required ltr inputMode="numeric"
            hint={t('onboarding.service.length', { n: slot })} />
          <TextField form={form} name="followUpFee" label={t('onboarding.field.followUpFee', { currency: rules.currency })} required ltr inputMode="numeric"
            hint={t('onboarding.service.length', { n: slot })} />
        </div>
        {/* shortcut: two fees only; more services (video, procedures) wait for the services catalogue (Settings, Services, P4). */}
        <p className="text-xs text-muted-foreground">{t('onboarding.hint.fees', { currency: rules.currency })} {t('onboarding.services.more')}</p>
      </Group>
    </div>
  );
}

type TeamProps = FormProps & { onInvite: () => void; onRemove: (i: number) => void };

const roleInfo: { id: 'frontDesk' | 'nurse' | 'doctor' | 'owner'; icon: LucideIcon }[] = [
  { id: 'frontDesk', icon: Headset }, { id: 'nurse', icon: HeartPulse }, { id: 'doctor', icon: Stethoscope }, { id: 'owner', icon: ShieldCheck },
];

/** Step 3: invite front desk and nurses through the invitation flow at finish; doctors are added in Settings (their licence is checked). */
export function TeamStep({ form, onInvite, onRemove }: TeamProps) {
  const { t } = useI18n();
  const team = useWatch({ control: form.control, name: 'team' });
  const errors = form.formState.errors;
  const inviteError = errors.inviteEmail?.message;
  return (
    <div className="flex flex-col gap-6">
      {team.length > 0 ? (
        <ul aria-label={t('onboarding.team.list')} className="flex flex-col divide-y rounded-xl border">
          {team.map((m, i) => (
            <li key={m.email} className="flex flex-col gap-1 px-4 py-2">
              <div className="flex min-h-11 items-center gap-3">
                <UserRound className="size-5 shrink-0 text-muted-foreground" aria-hidden />
                <span dir="ltr" className="min-w-0 flex-1 truncate text-sm font-semibold">{m.email}</span>
                <span className="rounded-full bg-muted px-2.5 py-1 text-xs font-semibold">{t(`onboarding.role.${m.role}`)}</span>
                <Button type="button" variant="ghost" size="icon" className="size-11" aria-label={t('onboarding.team.remove', { email: m.email })} onClick={() => onRemove(i)}>
                  <Trash2 className="size-4" aria-hidden />
                </Button>
              </div>
              <FieldError id={`team-${i}-error`} code={errors.team?.[i]?.email?.message} />
            </li>
          ))}
        </ul>
      ) : <p className="text-sm text-muted-foreground">{t('onboarding.team.empty')}</p>}
      <fieldset className="flex flex-col gap-3 rounded-xl border p-4">
        <legend className="px-1 text-sm font-bold">{t('onboarding.team.invite')}</legend>
        <Field id="inviteEmail" label={t('onboarding.field.inviteEmail')} error={inviteError} hint={t('onboarding.team.sent')}>
          {a => <Input {...a} {...form.register('inviteEmail')} type="email" inputMode="email" dir="ltr" autoComplete="off" spellCheck={false}
            onKeyDown={e => { if (e.key === 'Enter') { e.preventDefault(); onInvite(); } }} />}
        </Field>
        <div className="flex flex-wrap items-center gap-2">
          <div className="flex flex-wrap gap-2" role="radiogroup" aria-label={t('onboarding.field.inviteRole')}>
            {(['Receptionist', 'Nurse'] as const).map(r => (
              <label key={r} className={chip}>
                <input type="radio" value={r} {...form.register('inviteRole')} className="size-4 accent-brand-700" />{t(`onboarding.role.${r}`)}
              </label>
            ))}
          </div>
          <Button type="button" variant="outline" onClick={onInvite} className="ms-auto"><UserPlus className="size-4" aria-hidden />{t('onboarding.team.inviteCta')}</Button>
        </div>
        <Note icon={Stethoscope}>{t('onboarding.team.doctors')}</Note>
      </fieldset>
      <section aria-labelledby="roles-heading" className="flex flex-col gap-3">
        <h2 id="roles-heading" className="text-sm font-bold">{t('onboarding.roles.legend')}</h2>
        <ul className={grid}>
          {roleInfo.map(({ id, icon: Icon }) => (
            <li key={id} className="flex flex-col gap-1 rounded-xl border p-4 text-sm">
              <strong className="flex items-center gap-1.5"><Icon className="size-4" aria-hidden />{t(`onboarding.roleInfo.${id}`)}</strong>
              <span className="text-muted-foreground">{t(`onboarding.roleInfo.${id}.body`)}</span>
            </li>
          ))}
        </ul>
      </section>
    </div>
  );
}

const startIcons: Record<(typeof patientStarts)[number], LucideIcon> = { Upload: FileSpreadsheet, Move: ArrowLeftRight, Fresh: UserPlus };

/** Step 4: how the patients come in. Only the choice is kept (practice defaults); see the shortcut on PracticeDefaults.PatientStart. */
export function PatientsStep({ form }: FormProps) {
  const { t } = useI18n();
  const choice = useWatch({ control: form.control, name: 'patients' });
  return (
    <div className="flex flex-col gap-4">
      <Group legend={t('onboarding.patients.legend')} error={{ id: 'patients-error', code: form.formState.errors.patients?.message }}>
        <div className="grid gap-3 md:grid-cols-3">
          {patientStarts.map(p => {
            const Icon = startIcons[p];
            return (
              <label key={p} className={`${card} flex-col`}>
                <span className="flex items-center gap-2">
                  <input type="radio" value={p} {...form.register('patients')} className="size-4 accent-brand-700" />
                  <Icon className="size-5" aria-hidden />
                </span>
                <strong>{t(`onboarding.patients.${p}`)}</strong>
                <span className="text-muted-foreground">{t(`onboarding.patients.${p}.hint`)}</span>
              </label>
            );
          })}
        </div>
      </Group>
      {choice && <Note icon={choice === 'Upload' ? ClipboardList : Info}>{t(`onboarding.patients.note.${choice}`)}</Note>}
    </div>
  );
}

const connectionIcons: Record<string, LucideIcon> = { upi: QrCode, card: CreditCard, whatsapp: MessageCircle, abha: BadgeCheck, nhcx: ShieldCheck, eclaimlink: ShieldCheck, riayati: Building2 };

/** Step 5: what each country connects. Nothing is connected here yet (see the shortcut on `connections`), so each says "later". */
export function PaymentsStep({ country }: { country: Country }) {
  const { t } = useI18n();
  return (
    <ul className="flex flex-col gap-3">
      {connections[country].map(id => {
        const Icon = connectionIcons[id] ?? Info;
        return (
          <li key={id} className="flex items-start gap-3 rounded-xl border p-4 text-sm">
            <span className="flex size-10 shrink-0 items-center justify-center rounded-lg bg-brand-50 text-brand-800"><Icon className="size-5" aria-hidden /></span>
            <span className="flex min-w-0 flex-1 flex-col gap-0.5">
              <strong>{t(`onboarding.connection.${id}` as MessageKey)}</strong>
              <span className="text-muted-foreground">{t(`onboarding.connection.${id}.body` as MessageKey)}</span>
            </span>
            <span className="shrink-0 rounded-full bg-muted px-2.5 py-1 text-xs font-semibold">{t('onboarding.connection.later')}</span>
          </li>
        );
      })}
    </ul>
  );
}
