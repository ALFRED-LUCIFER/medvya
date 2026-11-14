import { useI18n } from '@medvya/i18n';
import { Button, Dialog, DialogContent, DialogDescription, DialogTitle, Switch } from '@medvya/ui';
import { fieldError } from './fieldStyles';
import { Section } from './Section';

type Props = {
  enabled: boolean; loading: boolean; forbidden: boolean; loadFailed: boolean; saving: boolean; saveFailed: boolean;
  withNurse: number | null; confirming: boolean | null; onChange: (enabled: boolean) => void; onConfirm: () => void; onCancel: () => void;
};

const switchId = 'nurse-intake-switch';

/**
 * Nurse intake before the doctor (MDV-P2-007): off until the owner turns it on; then reception sends patients to the nurse, who hands them off.
 * Flipping it asks to confirm first, saying how many patients are with the nurse and what happens to them; nothing changes until then (MDV-P2-055).
 */
export default function NurseIntakePanel(p: Props) {
  const { t } = useI18n();
  if (p.loading) return <p role="status">{t('settings.loading')}</p>;
  if (p.loadFailed) return <p role="alert" className={fieldError}>{t(p.forbidden ? 'settings.forbidden' : 'settings.loadFailed')}</p>;
  const turn = p.confirming ? 'on' : 'off';
  return (
    <Section title={t('settings.nurseIntake.title')} intro={t('settings.nurseIntake.intro')}>
      <label className="flex min-h-11 items-center gap-3 font-bold">
        <Switch id={switchId} checked={p.enabled} disabled={p.saving} onCheckedChange={p.onChange} />
        <span>{t('settings.nurseIntake.label')}</span>
        <span className="text-sm font-normal text-muted-foreground">{t(p.enabled ? 'settings.notifications.on' : 'settings.notifications.off')}</span>
      </label>
      <Dialog open={p.confirming !== null} onOpenChange={open => { if (!open) p.onCancel(); }}>
        <DialogContent closeLabel={t('settings.nurseIntake.confirm.close')}
          // Opened by the switch's state, not by a trigger: focus goes back to the switch.
          onCloseAutoFocus={e => { e.preventDefault(); document.getElementById(switchId)?.focus(); }}>
          <DialogTitle>{t(`settings.nurseIntake.confirm.${turn}.title`)}</DialogTitle>
          <DialogDescription className="flex flex-col gap-2">
            {p.withNurse !== null && <span className="font-bold">{t('settings.nurseIntake.confirm.withNurse', { count: p.withNurse })}</span>}
            <span>{t(`settings.nurseIntake.confirm.${turn}.body`)}</span>
          </DialogDescription>
          {p.saveFailed && <p role="alert" className={fieldError}>{t('settings.saveFailed')}</p>}
          <div className="flex flex-wrap gap-2">
            <Button loading={p.saving} onClick={p.onConfirm}>{t(`settings.nurseIntake.confirm.${turn}.action`)}</Button>
            <Button variant="outline" disabled={p.saving} onClick={p.onCancel}>{t('settings.nurseIntake.confirm.cancel')}</Button>
          </div>
        </DialogContent>
      </Dialog>
    </Section>
  );
}
