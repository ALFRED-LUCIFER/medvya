import { useI18n } from '@medvya/i18n';
import { Switch } from '@medvya/ui';
import { fieldError } from './fieldStyles';
import { Section } from './Section';

type Props = { enabled: boolean; loading: boolean; forbidden: boolean; loadFailed: boolean; saving: boolean; saveFailed: boolean; onChange: (enabled: boolean) => void };

/** Nurse intake before the doctor (MDV-P2-007): off until the owner turns it on; then reception sends patients to the nurse, who hands them off. */
export default function NurseIntakePanel(p: Props) {
  const { t } = useI18n();
  if (p.loading) return <p role="status">{t('settings.loading')}</p>;
  if (p.loadFailed) return <p role="alert" className={fieldError}>{t(p.forbidden ? 'settings.forbidden' : 'settings.loadFailed')}</p>;
  return (
    <Section title={t('settings.nurseIntake.title')} intro={t('settings.nurseIntake.intro')}>
      <label className="flex min-h-11 items-center gap-3 font-bold">
        <Switch checked={p.enabled} disabled={p.saving} onCheckedChange={p.onChange} />
        <span>{t('settings.nurseIntake.label')}</span>
        <span className="text-sm font-normal text-muted-foreground">{t(p.enabled ? 'settings.notifications.on' : 'settings.notifications.off')}</span>
      </label>
      {p.saveFailed && <p role="alert" className={fieldError}>{t('settings.saveFailed')}</p>}
    </Section>
  );
}
