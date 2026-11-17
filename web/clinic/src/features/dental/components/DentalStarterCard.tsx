import { useI18n } from '@medvya/i18n';
import { Button, Card } from '@medvya/ui';
import { CircleCheck } from 'lucide-react';
import { fieldError } from '../../settings/components/fieldStyles';
import type { StarterView } from '../domain/dental';

/** Settings › Dental: load the dental services starter list (MDV-P8-010), then review the fees in Settings › Services. */
export default function DentalStarterCard(p: StarterView) {
  const { t, formatNumber } = useI18n();
  return (
    <Card title={t('settings.services.dental.title')} intro={t('settings.services.dental.intro')}>
      {p.result && (
        <p role="status" className="flex items-center gap-2">
          <CircleCheck aria-hidden className="size-4 shrink-0" />
          {t('settings.services.dental.loaded', { added: formatNumber(p.result.added), skipped: formatNumber(p.result.skipped) })}
        </p>
      )}
      {p.unsupported && <p role="alert" className={fieldError}>{t('settings.services.dental.unsupported')}</p>}
      {p.failed && <p role="alert" className={fieldError}>{t('settings.services.dental.failed')}</p>}
      <div><Button variant="outline" loading={p.loading} onClick={p.onLoad}>{t('settings.services.dental.load')}</Button></div>
    </Card>
  );
}
