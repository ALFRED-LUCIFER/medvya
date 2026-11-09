import type { ReactNode } from 'react';
import { Link } from 'react-router';
import { useI18n } from '@medvya/i18n';
import { Button, buttonVariants } from '@medvya/ui';
import type { ClinicalRefusal } from '../../clinical-access/domain/clinicalAccess';
import type { EncounterBanner } from '../domain/encounter';
import IdentityBanner from './IdentityBanner';

type Props = {
  banner: EncounterBanner | null; loading: boolean; notFound: boolean; refusal: ClinicalRefusal | null; failed: boolean;
  ending: boolean; endFailed: boolean; onEnd: () => void;
  boardPath: string;
  /** The work of the visit (the note editor, MDV-P2-009), composed in by the route. */
  children?: ReactNode;
};

/**
 * The consultation (MDV-P2-001): the identity banner, then the work of the visit. Leaving keeps the consultation open (the doctor chose
 * to); ending it moves the visit on to Billing on the board. Notes and orders arrive here with MDV-P2-009 and MDV-P2-015.
 */
export default function ConsultPage(p: Props) {
  const { t, formatTime } = useI18n();
  if (p.loading) return <p role="status">{t('consult.loading')}</p>;
  if (p.refusal) return <Message title={t('consult.refused.title')} body={t(p.refusal.canRequest ? 'consult.refused.ask' : 'consult.refused.role')} boardPath={p.boardPath} />;
  if (p.notFound || p.failed || !p.banner) return <Message title={t(p.notFound ? 'consult.notFound' : 'consult.loadFailed')} boardPath={p.boardPath} />;
  const { encounter } = p.banner;
  const ended = encounter.status === 'Ended';
  return (
    <div className="flex flex-col gap-4">
      <IdentityBanner banner={p.banner} />
      <div className="flex flex-wrap items-center gap-3">
        {ended
          ? <p role="status" className="font-bold">{t('consult.ended', { time: formatTime(new Date(encounter.endedAt!), p.banner.timeZone) })}</p>
          : <Button className="min-h-11" loading={p.ending} onClick={p.onEnd}>{t('consult.end')}</Button>}
        <Link to={p.boardPath} className={buttonVariants({ variant: 'outline', className: 'min-h-11' })}>{t(ended ? 'consult.backToBoard' : 'consult.leave')}</Link>
      </div>
      {!ended && <p className="text-sm text-muted-foreground">{t('consult.leaveNote')}</p>}
      {p.endFailed && <p role="alert" className="font-bold text-danger-700">{t('consult.endFailed')}</p>}
      {p.children}
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
