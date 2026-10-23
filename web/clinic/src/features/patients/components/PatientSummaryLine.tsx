import { TriangleAlert } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import type { PatientSummary } from '../domain/directory';

/**
 * A privacy-safe patient row's content (MDV-P1-018): initials, age, sex, MRN tail and "incomplete" only, never the name.
 * The wrapping link or button gives the accessible name; this part is visual.
 */
export default function PatientSummaryLine({ row }: { row: PatientSummary }) {
  const { t } = useI18n();
  const age = t(row.ageEstimated ? 'patients.ageEstimated' : 'patients.age', { age: row.ageYears });
  const sex = t(`patients.sex.${row.sexAtBirth}`);
  return (
    <>
      <span aria-hidden className="grid size-9 shrink-0 place-items-center rounded-full bg-muted font-extrabold text-brand-700">{row.initials}</span>
      <span aria-hidden className="flex min-w-0 flex-1 flex-col">
        <span className="font-bold">{t('patients.row.ageSex', { age, sex })}</span>
        <span className="font-mono text-xs text-muted-foreground"><bdi dir="ltr">{t('patients.mrnTail', { mrn: row.mrnTail })}</bdi></span>
      </span>
      {row.status === 'Incomplete' && (
        <span aria-hidden className="inline-flex shrink-0 items-center gap-1 rounded-sm border bg-card px-2 py-1 text-xs font-bold text-warning-700">
          <TriangleAlert className="size-3.5" aria-hidden />{t('patients.status.Incomplete')}
        </span>
      )}
    </>
  );
}
