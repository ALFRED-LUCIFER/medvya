import type { ReactNode } from 'react';
import { useI18n } from '@medvya/i18n';
import { Card } from '@medvya/ui';
import type { DeskVisit } from '../domain/visits';

type Props = { visits: DeskVisit[]; timeZone?: string; loading: boolean; failed: boolean; followUp?: ReactNode };

/** The desk record's visits (MDV-1088): when and what state, never a reason, diagnosis or note; first, the doctor's follow-up still to book (MDV-P1-102). */
export default function DeskVisits(p: Props) {
  const { t, formatDate, formatTime } = useI18n();
  return (
    <Card title={t('visits.title')}>
      {p.followUp}
      {p.loading ? <p role="status">{t('visits.loading')}</p>
        : p.failed ? <p role="alert" className="text-sm font-semibold text-danger-700">{t('visits.loadFailed')}</p>
        : p.visits.length === 0 ? <p className="text-muted-foreground">{t('visits.empty')}</p>
        : (
          <ul className="flex flex-col divide-y">
            {p.visits.map(v => {
              const at = new Date(v.start);
              return (
                <li key={v.appointmentId} className="flex min-h-11 flex-wrap items-center justify-between gap-x-4 py-1">
                  <time dateTime={v.start} className="font-bold">{`${formatDate(at, p.timeZone)} ${formatTime(at, p.timeZone)}`}</time>
                  <span className="text-sm font-bold">{t(`schedule.state.${v.state}`)}</span>
                </li>
              );
            })}
          </ul>
        )}
    </Card>
  );
}
