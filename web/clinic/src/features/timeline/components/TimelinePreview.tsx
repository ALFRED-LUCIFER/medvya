import { Link } from 'react-router';
import { useI18n } from '@medvya/i18n';
import { buttonVariants } from '@medvya/ui';
import type { TimelineEntry } from '../domain/timeline';
import { Entry } from './TimelinePage';

type Props = { entries: TimelineEntry[]; timeZone: string; loading: boolean; failed: boolean; timelinePath: string };

/** The newest entries of the timeline (MDV-P2-005) on the one-page chart (MDV-P2-033), each with its source tag; filters and older entries are on the full timeline. */
export default function TimelinePreview(p: Props) {
  const { t } = useI18n();
  return (
    <section aria-label={t('timeline.title')} className="flex flex-col gap-3 rounded-xl border bg-card p-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 className="text-base font-extrabold">{t('timeline.title')}</h2>
        <Link to={p.timelinePath} className={buttonVariants({ variant: 'outline', className: 'min-h-11' })}>{t('timeline.openFull')}</Link>
      </div>
      {p.loading ? <p role="status">{t('timeline.loading')}</p>
        : p.failed ? <p role="alert" className="font-bold">{t('timeline.loadFailed')}</p>
        : p.entries.length === 0 ? <p className="text-muted-foreground">{t('timeline.empty')}</p>
        : <ol className="flex flex-col gap-3">{p.entries.slice(0, previewSize).map(e => <Entry key={e.id} entry={e} zone={p.timeZone} />)}</ol>}
    </section>
  );
}

const previewSize = 8;
