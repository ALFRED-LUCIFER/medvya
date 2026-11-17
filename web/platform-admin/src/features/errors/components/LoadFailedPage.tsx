import { Button, PageMessage } from '@medvya/ui';
import { useI18n } from '@medvya/i18n';

/** The console could not show this screen (most often a new release since the tab opened): one sentence and a reload. */
export default function LoadFailedPage({ onReload }: { onReload: () => void }) {
  const { t } = useI18n();
  return (
    <main className="grid min-h-screen place-items-center p-4">
      <PageMessage kind="error" title={t('platform.loadFailed.title')} action={<Button onClick={onReload}>{t('platform.loadFailed.reload')}</Button>}>
        {t('platform.loadFailed.body')}
      </PageMessage>
    </main>
  );
}
