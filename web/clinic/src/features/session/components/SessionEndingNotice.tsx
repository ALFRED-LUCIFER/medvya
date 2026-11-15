import { useI18n } from '@medvya/i18n';
import { Banner, Button } from '@medvya/ui';
import { Clock } from 'lucide-react';

type Props = { canStay: boolean; staying: boolean; failed: boolean; onStay: () => void };

/**
 * Shortly before the session ends (#1365 F-057): said in the shell, in the page's flow under the top bar, so it covers nothing and is
 * announced politely. No countdown: nothing moves while someone types. At the 8-hour limit there is nothing to extend, so it only says so.
 */
export default function SessionEndingNotice({ canStay, staying, failed, onStay }: Props) {
  const { t } = useI18n();
  return (
    <div className="px-4 pt-3 md:px-6 print:hidden">
      <Banner tone="warning" icon={Clock} title={t('session.ending.title')}
        action={canStay && <Button variant="outline" loading={staying} onClick={onStay}>{t('session.ending.stay')}</Button>}>
        {t(canStay ? 'session.ending.idle' : 'session.ending.limit')}
        {failed && <p role="alert" className="font-semibold">{t('session.ending.failed')}</p>}
      </Banner>
    </div>
  );
}
