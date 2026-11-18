import { useI18n } from '@medvya/i18n';
import { Card, Switch } from '@medvya/ui';
import { fieldError } from '../../settings/components/fieldStyles';
import type { DentalTemplateView } from '../domain/dental';

/** Settings › Dental: new notes start on the dental template (MDV-P8-015); a doctor's own choice of templates still wins. */
export default function DentalTemplateCard(p: DentalTemplateView) {
  const { t } = useI18n();
  if (p.loading) return <p role="status">{t('settings.loading')}</p>;
  if (p.loadFailed) return <p role="alert" className={fieldError}>{t('settings.loadFailed')}</p>;
  return (
    <Card title={t('settings.dental.template.title')} intro={t('settings.dental.template.intro')}>
      <label className="flex min-h-control items-center gap-3 font-bold">
        <Switch checked={p.on} disabled={p.saving} onCheckedChange={p.onChange} />
        <span>{t('settings.dental.template.label')}</span>
        <span className="text-sm font-normal text-muted-foreground">{t(p.on ? 'settings.notifications.on' : 'settings.notifications.off')}</span>
      </label>
      {p.saveFailed && <p role="alert" className={fieldError}>{t('settings.saveFailed')}</p>}
    </Card>
  );
}
