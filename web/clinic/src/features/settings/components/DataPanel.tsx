import { Link } from 'react-router';
import { useI18n } from '@medvya/i18n';
import { Card, Detail, DetailList, buttonVariants } from '@medvya/ui';

type Props = {
  /** The practice's country, from the server (null before sign-in finishes). */
  country: 'IN' | 'AE' | null;
  /** Where the access log is, when this role may open it. */
  accessLogPath: string;
};

/** Data (F-08): where the practice's data lives and how long it is kept. Read-only; nothing here can be changed. */
export default function DataPanel({ country, accessLogPath }: Props) {
  const { t } = useI18n();
  return (
    <div className="flex flex-col gap-4">
      <Card title={t('settings.data.residency')} intro={t('site.trust.residency.body')}>
        {country && (
          <DetailList layout="inline" className="md:gap-x-6">
            <Detail label={t('settings.data.country')}>{t(`settings.data.country.${country}`)}</Detail>
            <Detail label={t('settings.data.region')}>{t(`site.residency.${country}`)}</Detail>
          </DetailList>
        )}
        <p className="text-sm text-muted-foreground">{t('settings.data.fixed')}</p>
      </Card>
      <Card title={t('settings.data.retention')} intro={t('settings.data.retentionIntro')}>
        <p>{t('site.trust.retention.body')}</p>
      </Card>
      <Card title={t('settings.data.export')} intro={t('settings.data.exportLater')}>
        <p className="text-sm text-muted-foreground">{t('settings.data.exportNote')}</p>
      </Card>
      <Card title={t('settings.data.access')} intro={t('site.trust.audit.body')}>
        <Link to={accessLogPath} className={buttonVariants({ variant: 'outline', className: 'self-start' })}>{t('settings.data.openAccessLog')}</Link>
      </Card>
    </div>
  );
}
