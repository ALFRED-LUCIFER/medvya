import { useI18n } from '@medvya/i18n';
import { useParams } from 'react-router';
import ReconciliationPage from '../features/reconciliation/components/ReconciliationPage';
import { useReconciliationController } from '../features/reconciliation/hooks/useReconciliationController';
import TabletOnly from './TabletOnly';

/** Composition only: one consultation's medication reconciliation (MDV-P2-011, E-07) by its id in the address; the server checks clinical access on every call. */
export default function ReconciliationRoute({ boardPath, online, timeZone }: { boardPath: string; online: boolean; timeZone: string }) {
  const { encounterId = '' } = useParams();
  return <Reconcile key={encounterId} encounterId={encounterId} boardPath={boardPath} online={online} timeZone={timeZone} />;
}

function Reconcile({ encounterId, boardPath, online, timeZone }: { encounterId: string; boardPath: string; online: boolean; timeZone: string }) {
  const rec = useReconciliationController(encounterId);
  const { t } = useI18n();
  return (
    <TabletOnly title={t('reconcile.title')} patientName={rec.banner?.patient.fullName}>
      <ReconciliationPage {...rec} timeZone={timeZone} online={online} consultPath={`/consult/${encounterId}`} boardPath={boardPath} />
    </TabletOnly>
  );
}
