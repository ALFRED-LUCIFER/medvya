import { useI18n } from '@medvya/i18n';
import { Button } from '@medvya/ui';
import RequestAccess from '../../clinical-access/components/RequestAccess';
import type { AccessReason } from '../../clinical-access/domain/clinicalAccess';
import type { StartProblem } from '../domain/encounter';

type Props = {
  patientName: string; inConsult: boolean; online: boolean;
  /** Filled only on the one next patient of the view (one primary per view); outline otherwise. */
  primary?: boolean;
  problem: StartProblem | null; starting: boolean; reason: AccessReason; asking: boolean; askFailed: boolean;
  onStart: () => void; onLeaveOpenAndStart: () => void; onReturn: () => void; onReason: (r: AccessReason) => void; onRequestAccess: () => void;
};

/** "Start consultation" on a board card (MDV-P2-001), with what the doctor can do when it cannot start: return, leave open, or ask for access. */
export default function StartConsult(p: Props) {
  const { t } = useI18n();
  const label = t(p.inConsult ? 'consult.open' : 'consult.start');
  const problem = p.problem;
  return (
    <div className="flex flex-col gap-2">
      <Button variant={p.primary === false ? 'outline' : 'default'} className="whitespace-normal" loading={p.starting} disabled={!p.online}
        aria-label={t(p.inConsult ? 'consult.openFor' : 'consult.startFor', { name: p.patientName })} onClick={p.onStart}>{label}</Button>
      {problem?.kind === 'open' && (
        <div role="alert" className="flex flex-col gap-2 rounded-md border p-2 text-sm">
          <p className="font-bold">{t('consult.openElsewhere')}</p>
          <Button variant="outline" className="whitespace-normal" onClick={p.onReturn}>{t('consult.returnToOpen')}</Button>
          <Button variant="outline" className="whitespace-normal" loading={p.starting} onClick={p.onLeaveOpenAndStart}>{t('consult.leaveOpenAndStart')}</Button>
        </div>
      )}
      {problem?.kind === 'refused' && (
        <div role="alert" className="flex flex-col gap-2 rounded-md border p-2 text-sm">
          <RequestAccess refusal={problem.refusal} roleMessage={t('consult.refused.role')} reason={p.reason} asking={p.asking} askFailed={p.askFailed} onReason={p.onReason} onRequestAccess={p.onRequestAccess} />
        </div>
      )}
      {problem?.kind === 'visit' && <p role="alert" className="text-xs font-bold text-danger-700">{t(`consult.visit.${problem.code}`)}</p>}
      {problem?.kind === 'failed' && <p role="alert" className="text-xs font-bold text-danger-700">{t('consult.failed')}</p>}
    </div>
  );
}
