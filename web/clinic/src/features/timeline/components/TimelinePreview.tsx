import { Link } from 'react-router';
import { useI18n } from '@medvya/i18n';
import { buttonVariants, Card } from '@medvya/ui';
import type { TimelineEntry } from '../domain/timeline';
import { Entry } from './TimelinePage';

type Props = { entries: TimelineEntry[]; timeZone: string; loading: boolean; failed: boolean; timelinePath: string; consultPath: (encounterId: string) => string; printRx: boolean };

/** The newest entries of the timeline (MDV-P2-005) on the one-page chart (MDV-P2-033), each with its source tag; filters and older entries are on the full timeline. */
export default function TimelinePreview(p: Props) {
  const { t } = useI18n();
  return (
    <Card title={t('timeline.title')} actions={<Link to={p.timelinePath} className={buttonVariants({ variant: 'outline' })}>{t('timeline.openFull')}</Link>}>
      {p.loading ? <p role="status">{t('timeline.loading')}</p>
        : p.failed ? <p role="alert" className="font-bold">{t('timeline.loadFailed')}</p>
        : p.entries.length === 0 ? <p className="text-muted-foreground">{t('timeline.empty')}</p>
        : <ol className="flex flex-col gap-3">{p.entries.slice(0, previewSize).map(e => <Entry key={e.id} entry={e} zone={p.timeZone} consultPath={p.consultPath} printRx={p.printRx} />)}</ol>}
    </Card>
  );
}

const previewSize = 8;
