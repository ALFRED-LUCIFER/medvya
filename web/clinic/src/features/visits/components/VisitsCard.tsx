import { Link } from 'react-router';
import { useI18n } from '@medvya/i18n';
import { buttonVariants } from '@medvya/ui';
import { visitStatus, type VisitRow } from '../domain/visits';

type Props = { visits: VisitRow[]; timeZone: string; loading: boolean; failed: boolean; consultPath: (encounterId: string) => string; printRx: boolean };

/** The chart's Visits block (MDV-1088): each past visit with its doctor, diagnosis, medicines and tests; a row opens the visit (the signed note, read-only) and its prescription. */
export default function VisitsCard(p: Props) {
  const { t, formatDate, formatTime } = useI18n();
  const link = buttonVariants({ variant: 'link', className: 'min-h-11 px-0' });
  return (
    <section aria-label={t('visits.title')} className="flex flex-col gap-3 rounded-xl border bg-card p-4">
      <h2 className="text-base font-extrabold">{t('visits.title')}</h2>
      {p.loading ? <p role="status">{t('visits.loading')}</p>
        : p.failed ? <p role="alert" className="font-bold">{t('visits.loadFailed')}</p>
        : p.visits.length === 0 ? <p className="text-muted-foreground">{t('visits.empty')}</p>
        : (
          <ol className="flex flex-col gap-3">
            {p.visits.map(v => {
              const when = new Date(v.startedAt);
              const status = visitStatus(v);
              return (
                <li key={v.encounterId} className="flex flex-col gap-1 border-b pb-3 last:border-b-0">
                  <div className="flex flex-wrap items-baseline justify-between gap-x-4">
                    <time dateTime={v.startedAt} className="font-bold">{`${formatDate(when, p.timeZone)} ${formatTime(when, p.timeZone)}`}</time>
                    <span className="text-sm font-bold">{t(`visits.status.${status}`)}</span>
                  </div>
                  {v.doctor && <span className="text-sm text-muted-foreground">{v.doctor}</span>}
                  <Line label={t('visits.diagnosis')} items={v.diagnoses.map(d => (d.code ? `${d.description} (${d.code})` : d.description))} none={t('visits.none')} />
                  <Line label={t('visits.medicines')} items={v.medicines.map(m => [m.name, m.dose].filter(Boolean).join(' '))} none={t('visits.none')} />
                  <Line label={t('visits.tests')} items={v.tests} none={t('visits.none')} />
                  <span className="flex flex-wrap gap-x-4">
                    <Link to={p.consultPath(v.encounterId)} className={link}>{t(status === 'signed' ? 'visits.openSigned' : 'visits.open')}</Link>
                    {p.printRx && v.medicines.length > 0 && <Link to={`${p.consultPath(v.encounterId)}/rx/print`} className={link}>{t('medicine.prescribed.print')}</Link>}
                  </span>
                </li>
              );
            })}
          </ol>
        )}
    </section>
  );
}

function Line({ label, items, none }: { label: string; items: string[]; none: string }) {
  return (
    <p className="text-sm">
      <span className="font-bold">{`${label}: `}</span>
      {items.length === 0 ? <span className="text-muted-foreground">{none}</span> : items.map((x, i) => <span key={i}>{i > 0 && ', '}<bdi>{x}</bdi></span>)}
    </p>
  );
}
