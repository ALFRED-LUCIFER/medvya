import { useI18n } from '@medvya/i18n';
import { Button, PageContainer, PageHeader, PageMessage } from '@medvya/ui';

/** A screen this role may not open (MDV-P1-096). It never says what the screen holds or whose it is; the server's 403 is the control. */
export default function DeniedPage({ onHome }: { onHome: () => void }) {
  const { t } = useI18n();
  return (
    <PageContainer width="form">
      <PageHeader title={t('denied.title')} />
      <PageMessage kind="denied" action={<Button onClick={onHome}>{t('state.notFound.cta')}</Button>}>{t('denied.body')}</PageMessage>
    </PageContainer>
  );
}
