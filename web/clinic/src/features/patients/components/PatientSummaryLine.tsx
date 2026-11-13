import { TriangleAlert } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import type { PatientSummary } from '../domain/directory';

/**
 * A result row's content (MDV-AUDIT-1100): the full name first, then age, sex, and the last four of the phone and the MRN, so two people with the same
 * name still differ. The wrapping button gives the accessible name; this part is visual.
 */
export default function PatientSummaryLine({ row }: { row: PatientSummary }) {
  const { t } = useI18n();
  const age = t(row.ageEstimated ? 'patients.ageEstimated' : 'patients.age', { age: row.ageYears });
  const sex = t(`patients.sex.${row.sexAtBirth}`);
  return (
    <>
      {/* No initials badge: the name is shown, and the button's accessible name must hold exactly the visible words (axe label-content-name-mismatch). */}
      {/* Wraps so a narrow column (the booking panel) puts the badge on its own line instead of squeezing the details. */}
      <span aria-hidden className="flex min-w-0 flex-1 flex-wrap items-center justify-between gap-x-3 gap-y-1">
        <span className="flex min-w-0 flex-col">
          <span className="break-words font-bold">{row.fullName}</span>{' '}
          <span className="text-sm">{t('patients.row.ageSex', { age, sex })}</span>{' '}
          <span className="font-mono text-xs text-muted-foreground">
            <bdi dir="ltr">{t('patients.mrnTail', { mrn: row.mrnTail })}</bdi>{' · '}<bdi dir="ltr">{t('patients.mobileTail', { phone: row.mobileTail })}</bdi>
          </span>
        </span>
        {row.status === 'Incomplete' && (
          <span className="inline-flex items-center gap-1 rounded-sm border bg-card px-2 py-1 text-xs font-bold text-warning-700">
            <TriangleAlert className="size-3.5" aria-hidden />{t('patients.status.Incomplete')}
          </span>
        )}
      </span>
    </>
  );
}
