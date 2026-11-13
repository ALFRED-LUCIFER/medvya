import { useI18n } from '@medvya/i18n';
import { Button, Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@medvya/ui';
import { accessReasons, type AccessReason, type ClinicalRefusal } from '../domain/clinicalAccess';

type Props = {
  refusal: ClinicalRefusal; reason: AccessReason; asking: boolean; askFailed: boolean;
  /** What a role that cannot treat is told, in the screen's own words. */
  roleMessage: string;
  onReason: (r: AccessReason) => void; onRequestAccess: () => void;
};

/**
 * What a refused clinician can do (MDV-P2-024): nothing (the role cannot treat), wait (already asked), or say why and ask an owner.
 * The server decides each case; this only shows it.
 */
export default function RequestAccess(p: Props) {
  const { t } = useI18n();
  if (!p.refusal.canRequest) return <p className="font-bold">{p.roleMessage}</p>;
  if (p.refusal.requestPending) return <p className="font-bold">{t('consult.refused.pending')}</p>;
  return (
    <>
      <p className="font-bold">{t('consult.refused.ask')}</p>
      <Select value={p.reason} onValueChange={v => p.onReason(v as AccessReason)}>
        <SelectTrigger className="w-full" aria-label={t('consult.refused.reason')}><SelectValue /></SelectTrigger>
        <SelectContent>{accessReasons.map(r => <SelectItem key={r} value={r}>{t(`clinicalAccess.reason.${r}`)}</SelectItem>)}</SelectContent>
      </Select>
      <Button className="whitespace-normal" loading={p.asking} onClick={p.onRequestAccess}>{t('state.noAccess.cta')}</Button>
      {p.askFailed && <p className="font-bold text-danger-700">{t('consult.failed')}</p>}
    </>
  );
}
