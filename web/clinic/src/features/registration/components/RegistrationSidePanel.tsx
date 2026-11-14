import { Check, UserRound } from 'lucide-react';
import { useI18n, type Country } from '@medvya/i18n';
import { Card } from '@medvya/ui';
import { ageOn, isSex, rules, type RegistrationForm } from '../domain/registration';

type Props = { country: Country; values: RegistrationForm; ageInstead: boolean; today: string };

/** P-02 side panel: preview card and the country's rules (the developer-facing data map is gone, MDV-P1-052). Shows only what the desk typed. */
export default function RegistrationSidePanel({ country, values, ageInstead, today }: Props) {
  const { t } = useI18n();
  const age = ageInstead ? (values.ageYears.trim() === '' ? null : Number(values.ageYears)) : ageOn(values.dateOfBirth, today);
  const meta = [age !== null && !Number.isNaN(age) ? t(ageInstead ? 'patients.ageEstimated' : 'patients.age', { age }) : '',
    isSex(values.sexAtBirth) ? t(`patients.sex.${values.sexAtBirth}`) : ''].filter(Boolean).join(' · ');
  return (
    <aside aria-label={t('registration.preview.title')} className="flex flex-col gap-4">
      <Card title={t('registration.preview.title')}>
        <div className="flex items-center gap-3">
          <UserRound className="size-10 shrink-0 rounded-full bg-muted p-2 text-muted-foreground" aria-hidden />
          <div className="min-w-0">
            <p className="font-bold break-words"><bdi>{values.fullName.trim() || t('registration.preview.noName')}</bdi></p>
            {meta && <p className="text-sm text-muted-foreground">{meta}</p>}
            {country === 'AE' && values.arabicName.trim() && <p dir="rtl" lang="ar" className="text-sm">{values.arabicName}</p>}
          </div>
        </div>
        <p className="text-xs text-muted-foreground">{t('registration.preview.mrn')}</p>
      </Card>


      <Card title={t('registration.rules.title')}>
        <ul className="flex flex-col gap-1.5">
          {rules[country].map(r => (
            <li key={r} className="flex items-start gap-2 text-sm"><Check className="mt-0.5 size-4 shrink-0 text-success-700" aria-hidden />{t(`registration.rules.${r}`)}</li>
          ))}
        </ul>
      </Card>
    </aside>
  );
}
