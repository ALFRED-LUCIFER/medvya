import type { ReactNode } from 'react';
import { Link } from 'react-router';
import { CircleAlert } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, buttonVariants } from '@medvya/ui';
import type { ClinicalRefusal } from '../../clinical-access/domain/clinicalAccess';
import { openPastDay, type ConsultView, type EncounterBanner } from '../domain/encounter';
import IdentityBanner from './IdentityBanner';

type Props = {
  banner: EncounterBanner | null; loading: boolean; notFound: boolean; refusal: ClinicalRefusal | null; failed: boolean;
  ending: boolean; endFailed: boolean; onEnd: () => void;
  boardPath: string; canReconcile: boolean;
  /** One page or the guided steps (MDV-P2-041): the same note either way. */
  view: ConsultView; onView: (view: ConsultView) => void;
  /** The work of the visit (the note editor, MDV-P2-009), composed in by the route. */
  children?: ReactNode;
};

/**
 * The consultation (MDV-P2-001): the identity banner, then the work of the visit. Leaving keeps the consultation open (the doctor chose
 * to); ending it moves the visit on the board (to Billing, or Done when billing is off). Ending is a secondary action under the note, beside Sign
 * (MDV audit UX-03): the note is the work, the page's one primary is Sign. Notes and orders arrive here with MDV-P2-009 and MDV-P2-015.
 */
export default function ConsultPage(p: Props) {
  const { t, formatDate, formatTime } = useI18n();
  if (p.loading) return <p role="status">{t('consult.loading')}</p>;
  if (p.refusal) return <Message title={t('consult.refused.title')} body={t(p.refusal.canRequest ? 'consult.refused.ask' : 'consult.refused.role')} boardPath={p.boardPath} />;
  if (p.notFound || p.failed || !p.banner) return <Message title={t(p.notFound ? 'consult.notFound' : 'consult.loadFailed')} boardPath={p.boardPath} />;
  const { encounter } = p.banner;
  const ended = encounter.status === 'Ended';
  return (
    <div className="flex flex-col gap-4">
      <IdentityBanner banner={p.banner} />
      <div className="flex flex-wrap items-center gap-3">
        {ended && <p role="status" className="font-bold">{t('consult.ended', { time: formatTime(new Date(encounter.endedAt!), p.banner.timeZone) })}</p>}
        {p.canReconcile && <Link to={`/consult/${encounter.id}/problems`} className={buttonVariants({ variant: 'outline', className: 'min-h-11' })}>{t('consult.codeProblems')}</Link>}
        {p.canReconcile && <Link to={`/consult/${encounter.id}/medicines`} className={buttonVariants({ variant: 'outline', className: 'min-h-11' })}>{t('consult.reconcileMedicines')}</Link>}
        <Link to={p.boardPath} className={buttonVariants({ variant: 'outline', className: 'min-h-11' })}>{t(ended ? 'consult.backToBoard' : 'consult.leave')}</Link>
        <div role="group" aria-label={t('studio.view.label')} className="ms-auto flex gap-1">
          {(['page', 'steps'] as const).map(v => (
            <Button key={v} variant={p.view === v ? 'selected' : 'outline'} className="min-h-11" aria-pressed={p.view === v} onClick={() => p.onView(v)}>{t(`studio.view.${v}`)}</Button>
          ))}
        </div>
      </div>
      {openPastDay(encounter, p.banner.timeZone) && (
        <p role="status" className="inline-flex items-center gap-2 rounded-lg border border-warning-700 p-3 font-bold">
          <CircleAlert aria-hidden className="size-4 shrink-0" />{t('consult.openPastDay', { date: formatDate(new Date(encounter.startedAt), p.banner.timeZone) })}
        </p>
      )}
      {!ended && <p className="text-sm text-muted-foreground">{t('consult.leaveNote')}</p>}
      {p.children}
      {!ended && (
        <div className="flex flex-wrap items-center gap-3 border-t pt-4">
          <Button variant="outline" className="min-h-11" loading={p.ending} onClick={p.onEnd}>{t('consult.end')}</Button>
        </div>
      )}
      {p.endFailed && <p role="alert" className="font-bold text-danger-700">{t('consult.endFailed')}</p>}
    </div>
  );
}

function Message({ title, body, boardPath }: { title: string; body?: string; boardPath: string }) {
  const { t } = useI18n();
  return (
    <div role="alert" className="flex flex-col items-start gap-3 rounded-xl border bg-card p-6">
      <h1 className="text-lg font-extrabold">{title}</h1>
      {body && <p>{body}</p>}
      <Link to={boardPath} className={buttonVariants({ variant: 'outline', className: 'min-h-11' })}>{t('consult.backToBoard')}</Link>
    </div>
  );
}
