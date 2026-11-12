import { Link } from 'react-router';
import { CircleCheck, Hourglass, ShieldAlert } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { buttonVariants, ProvenanceTag } from '@medvya/ui';
import { provenanceOf } from '../../patient360/domain/chart';
import type { DocumentRow } from '../domain/documents';

type Props = { documents: DocumentRow[]; loading: boolean; failed: boolean; documentsPath: string };

/** The newest documents (MDV-P2-006) in the chart's right-hand column (MDV-P2-033): type, date, scan state and source. Upload and open are on the documents page. */
export default function DocumentsSummary(p: Props) {
  const { t, formatCalendarDate } = useI18n();
  return (
    <section aria-label={t('documents.title')} className="flex flex-col gap-3 rounded-xl border bg-card p-4">
      <h2 className="text-base font-extrabold">{t('documents.title')}</h2>
      {p.loading ? <p role="status">{t('documents.loading')}</p>
        : p.failed ? <p role="alert" className="font-bold">{t('documents.loadFailed')}</p>
        : p.documents.length === 0 ? <p className="text-muted-foreground">{t('documents.empty')}</p>
        : (
          <ul className="flex flex-col gap-3">
            {p.documents.slice(0, previewSize).map(d => (
              <li key={d.id} className="flex flex-col gap-1 border-b pb-3 last:border-b-0">
                <strong>{t(`documents.type.${d.type}`)}</strong>
                <span className="text-sm">{t('documents.documentDate', { date: formatCalendarDate(d.documentDate) })}</span>
                <span className="flex flex-wrap items-center gap-2">
                  <ProvenanceTag source={provenanceOf[d.source]} />
                  <span className="inline-flex items-center gap-1.5 text-sm font-bold">
                    {d.scanStatus === 'Clean' ? <CircleCheck aria-hidden className="size-4" /> : d.scanStatus === 'Pending' ? <Hourglass aria-hidden className="size-4" /> : <ShieldAlert aria-hidden className="size-4" />}
                    {t(`documents.scan.${d.scanStatus}`)}
                  </span>
                </span>
              </li>))}
          </ul>
        )}
      <Link to={p.documentsPath} className={buttonVariants({ variant: 'outline', className: 'min-h-11 self-start' })}>{t('documents.openAll')}</Link>
    </section>
  );
}

const previewSize = 5;
