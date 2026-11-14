import { TriangleAlert } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Tag } from '@medvya/ui';
import type { PatientSummary } from '../domain/directory';

/**
 * A result row's content on two lines (MDV-AUDIT-1100, MDV-P1-101): the full name with age and sex, then the last four of the MRN and the phone,
 * and the last visit, so two people with the same name still differ. No date of birth: a list is read at a shared desk (decision 15). A record in an open merge request says so in words.
 * In a picker the wrapping button gives the accessible name and this part is visual; `spoken` makes it the name itself (the header search's options).
 */
export default function PatientSummaryLine({ row, spoken = false }: { row: Omit<PatientSummary, 'status'> & Partial<Pick<PatientSummary, 'status'>>; spoken?: boolean }) {
  const { t, formatCalendarDate } = useI18n();
  const age = t(row.ageEstimated ? 'patients.ageEstimated' : 'patients.age', { age: row.ageYears });
  const sex = t(`patients.sex.${row.sexAtBirth}`);
  return (
    <>
      {/* No initials badge: the name is shown, and the button's accessible name must hold exactly the visible words (axe label-content-name-mismatch). */}
      {/* The spaces between parts keep the words apart in the text axe compares with the button's name (label-content-name-mismatch). */}
      {/* Wraps so a narrow column (the booking panel) puts the badges on their own line instead of squeezing the details. */}
      <span aria-hidden={!spoken || undefined} className="flex min-w-0 flex-1 flex-wrap items-center justify-between gap-x-3 gap-y-1">
        <span className="flex min-w-0 flex-col">
          <span><span className="break-words font-bold">{row.fullName}</span>{' '}<span className="text-sm">{t('patients.row.ageSex', { age, sex })}</span></span>{' '}
          <span className="font-mono text-xs text-muted-foreground">
            <bdi dir="ltr">{t('patients.mrnTail', { mrn: row.mrnTail })}</bdi>{' · '}<bdi dir="ltr">{t('patients.mobileTail', { phone: row.mobileTail })}</bdi>{' · '}
            {row.lastVisitOn ? t('patients.row.lastVisit', { date: formatCalendarDate(row.lastVisitOn) }) : t('patients.row.noVisit')}
          </span>
        </span>{' '}
        {(row.duplicate || row.status === 'Incomplete') && (
          <span className="flex flex-wrap gap-1">
            {row.duplicate === 'PossibleDuplicate' && <Tag size="sm" tone="warning" icon={TriangleAlert} label={t('patients.duplicate.PossibleDuplicate')} />}{' '}
            {row.duplicate === 'MainRecord' && <Tag size="sm" tone="neutral" label={t('patients.duplicate.MainRecord')} />}{' '}
            {row.status === 'Incomplete' && <Tag size="sm" tone="warning" icon={TriangleAlert} label={t('patients.status.Incomplete')} />}
          </span>
        )}
      </span>
    </>
  );
}
