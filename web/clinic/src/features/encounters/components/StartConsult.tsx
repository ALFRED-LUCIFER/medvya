import { useI18n } from '@medvya/i18n';
import { Button, Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@medvya/ui';
import { accessReasons, type AccessReason } from '../../clinical-access/domain/clinicalAccess';
import type { StartProblem } from '../domain/encounter';

type Props = {
  patientName: string; inConsult: boolean; online: boolean;
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
      <Button variant="navy" className="min-h-11 whitespace-normal" loading={p.starting} disabled={!p.online}
        aria-label={t(p.inConsult ? 'consult.openFor' : 'consult.startFor', { name: p.patientName })} onClick={p.onStart}>{label}</Button>
      {problem?.kind === 'open' && (
        <div role="alert" className="flex flex-col gap-2 rounded-md border p-2 text-sm">
          <p className="font-bold">{t('consult.openElsewhere')}</p>
          <Button variant="outline" className="min-h-11 whitespace-normal" onClick={p.onReturn}>{t('consult.returnToOpen')}</Button>
          <Button variant="outline" className="min-h-11 whitespace-normal" loading={p.starting} onClick={p.onLeaveOpenAndStart}>{t('consult.leaveOpenAndStart')}</Button>
        </div>
      )}
      {problem?.kind === 'refused' && (
        <div role="alert" className="flex flex-col gap-2 rounded-md border p-2 text-sm">
          {!problem.refusal.canRequest ? <p className="font-bold">{t('consult.refused.role')}</p>
            : problem.refusal.requestPending ? <p className="font-bold">{t('consult.refused.pending')}</p>
            : <>
                <p className="font-bold">{t('consult.refused.ask')}</p>
                <Select value={p.reason} onValueChange={v => p.onReason(v as AccessReason)}>
                  <SelectTrigger className="h-11 w-full" aria-label={t('consult.refused.reason')}><SelectValue /></SelectTrigger>
                  <SelectContent>{accessReasons.map(r => <SelectItem key={r} value={r}>{t(`clinicalAccess.reason.${r}`)}</SelectItem>)}</SelectContent>
                </Select>
                <Button className="min-h-11 whitespace-normal" loading={p.asking} onClick={p.onRequestAccess}>{t('state.noAccess.cta')}</Button>
                {p.askFailed && <p className="font-bold text-danger-700">{t('consult.failed')}</p>}
              </>}
        </div>
      )}
      {problem?.kind === 'visit' && <p role="alert" className="text-xs font-bold text-danger-700">{t(`consult.visit.${problem.code}`)}</p>}
      {problem?.kind === 'failed' && <p role="alert" className="text-xs font-bold text-danger-700">{t('consult.failed')}</p>}
    </div>
  );
}
