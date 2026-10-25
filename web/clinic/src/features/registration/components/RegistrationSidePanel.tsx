import { Check, UserRound } from 'lucide-react';
import { useI18n, type Country } from '@medvya/i18n';
import { ageOn, dataMap, isSex, rules, type RegistrationForm, type Step } from '../domain/registration';

type Props = { country: Country; values: RegistrationForm; ageInstead: boolean; step: Step | null; today: string };

/** P-02 side panel: preview card, "Data map · this step writes" and the country's rules. Shows only what the desk typed. */
export default function RegistrationSidePanel({ country, values, ageInstead, step, today }: Props) {
  const { t } = useI18n();
  const age = ageInstead ? (values.ageYears.trim() === '' ? null : Number(values.ageYears)) : ageOn(values.dateOfBirth, today);
  const meta = [age !== null && !Number.isNaN(age) ? t(ageInstead ? 'patients.ageEstimated' : 'patients.age', { age }) : '',
    isSex(values.sexAtBirth) ? t(`patients.sex.${values.sexAtBirth}`) : ''].filter(Boolean).join(' · ');
  return (
    <aside aria-labelledby="registration-preview" className="flex flex-col gap-4">
      <section className="flex flex-col gap-2 rounded-xl border bg-card p-4">
        <h2 id="registration-preview" className="text-sm font-bold text-muted-foreground">{t('registration.preview.title')}</h2>
        <div className="flex items-center gap-3">
          <UserRound className="size-10 shrink-0 rounded-full bg-muted p-2 text-muted-foreground" aria-hidden />
          <div className="min-w-0">
            <p className="font-bold break-words"><bdi>{values.fullName.trim() || t('registration.preview.noName')}</bdi></p>
            {meta && <p className="text-sm text-muted-foreground">{meta}</p>}
            {country === 'AE' && values.arabicName.trim() && <p dir="rtl" lang="ar" className="text-sm">{values.arabicName}</p>}
          </div>
        </div>
        <p className="text-xs text-muted-foreground">{t('registration.preview.mrn')}</p>
      </section>

      <section aria-labelledby="registration-map" className="flex flex-col gap-2 rounded-xl border bg-card p-4">
        <h2 id="registration-map" className="text-sm font-bold text-muted-foreground">{t('registration.map.title')}</h2>
        <ul className="flex flex-col gap-2">
          {dataMap[step ?? 'quick'].map(d => (
            <li key={d.entity + d.key} className="text-sm">
              <span className="font-mono text-xs font-bold" dir="ltr">{d.entity}</span>
              <span className="block text-muted-foreground">{t(`registration.map.${d.key}`)}</span>
            </li>
          ))}
        </ul>
      </section>

      <section aria-labelledby="registration-rules" className="flex flex-col gap-2 rounded-xl border bg-card p-4">
        <h2 id="registration-rules" className="text-sm font-bold text-muted-foreground">{t('registration.rules.title')}</h2>
        <ul className="flex flex-col gap-1.5">
          {rules[country].map(r => (
            <li key={r} className="flex items-start gap-2 text-sm"><Check className="mt-0.5 size-4 shrink-0 text-success-700" aria-hidden />{t(`registration.rules.${r}`)}</li>
          ))}
        </ul>
      </section>
    </aside>
  );
}
