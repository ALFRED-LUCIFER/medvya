import { Link } from 'react-router';
import { useI18n } from '@medvya/i18n';
import { buttonVariants } from '@medvya/ui';
import { Section } from './parts';

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
      <Section title={t('settings.data.residency')} intro={t('site.trust.residency.body')}>
        {country && (
          <dl className="grid gap-2 sm:grid-cols-[max-content_1fr] sm:gap-x-6">
            <dt className="font-bold">{t('settings.data.country')}</dt>
            <dd>{t(`settings.data.country.${country}`)}</dd>
            <dt className="font-bold">{t('settings.data.region')}</dt>
            <dd>{t(`site.residency.${country}`)}</dd>
          </dl>
        )}
        <p className="text-sm text-muted-foreground">{t('settings.data.fixed')}</p>
      </Section>
      <Section title={t('settings.data.retention')} intro={t('settings.data.retentionIntro')}>
        <p>{t('site.trust.retention.body')}</p>
      </Section>
      <Section title={t('settings.data.export')} intro={t('settings.data.exportLater')}>
        <p className="text-sm text-muted-foreground">{t('settings.data.exportNote')}</p>
      </Section>
      <Section title={t('settings.data.access')} intro={t('site.trust.audit.body')}>
        <Link to={accessLogPath} className={buttonVariants({ variant: 'outline', className: 'self-start' })}>{t('settings.data.openAccessLog')}</Link>
      </Section>
    </div>
  );
}
