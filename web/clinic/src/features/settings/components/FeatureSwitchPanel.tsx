import { useI18n } from '@medvya/i18n';
import { Card, Button, Dialog, DialogContent, DialogDescription, DialogTitle, Switch } from '@medvya/ui';
import { fieldError } from './fieldStyles';

type Props = {
  /** Whose words: nurse intake (MDV-P2-007) or billing (MDV-P4-002, #1430). */
  copy: 'nurseIntake' | 'billingSwitch' | 'dental';
  enabled: boolean; loading: boolean; forbidden: boolean; unavailable: boolean; loadFailed: boolean; saving: boolean; saveFailed: boolean;
  withNurse: number | null; confirming: boolean | null; onChange: (enabled: boolean) => void; onConfirm: () => void; onCancel: () => void;
};

/**
 * A practice-wide switch the owner turns on (nurse intake before the doctor, billing): off until then. Flipping it asks to confirm first, saying
 * what happens (for nurse intake, how many patients are with the nurse); nothing changes until then (MDV-P2-055).
 */
export default function FeatureSwitchPanel(p: Props) {
  const { t } = useI18n();
  const switchId = `${p.copy}-switch`;
  if (p.loading) return <p role="status">{t('settings.loading')}</p>;
  if (p.unavailable) return <Card title={t(`settings.${p.copy}.title`)}><p role="status">{t(`settings.${p.copy}.unavailable`)}</p></Card>;
  if (p.loadFailed) return <p role="alert" className={fieldError}>{t(p.forbidden ? 'settings.forbidden' : 'settings.loadFailed')}</p>;
  const turn = p.confirming ? 'on' : 'off';
  return (
    <Card title={t(`settings.${p.copy}.title`)} intro={t(`settings.${p.copy}.intro`)}>
      <label className="flex min-h-control items-center gap-3 font-bold">
        <Switch id={switchId} checked={p.enabled} disabled={p.saving} onCheckedChange={p.onChange} />
        <span>{t(`settings.${p.copy}.label`)}</span>
        <span className="text-sm font-normal text-muted-foreground">{t(p.enabled ? 'settings.notifications.on' : 'settings.notifications.off')}</span>
      </label>
      <Dialog open={p.confirming !== null} onOpenChange={open => { if (!open) p.onCancel(); }}>
        <DialogContent closeLabel={t(`settings.${p.copy}.confirm.close`)}
          // Opened by the switch's state, not by a trigger: focus goes back to the switch.
          onCloseAutoFocus={e => { e.preventDefault(); document.getElementById(switchId)?.focus(); }}>
          <DialogTitle>{t(`settings.${p.copy}.confirm.${turn}.title`)}</DialogTitle>
          <DialogDescription className="flex flex-col gap-2">
            {p.withNurse !== null && <span className="font-bold">{t('settings.nurseIntake.confirm.withNurse', { count: p.withNurse })}</span>}
            <span>{t(`settings.${p.copy}.confirm.${turn}.body`)}</span>
          </DialogDescription>
          {p.saveFailed && <p role="alert" className={fieldError}>{t('settings.saveFailed')}</p>}
          <div className="flex flex-wrap gap-2">
            <Button loading={p.saving} onClick={p.onConfirm}>{t(`settings.${p.copy}.confirm.${turn}.action`)}</Button>
            <Button variant="outline" disabled={p.saving} onClick={p.onCancel}>{t(`settings.${p.copy}.confirm.cancel`)}</Button>
          </div>
        </DialogContent>
      </Dialog>
    </Card>
  );
}
