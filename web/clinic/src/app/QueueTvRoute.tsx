import type { Locale } from '@medvya/i18n';
import { useConnectivityController } from '../features/connectivity/hooks/useConnectivityController';
import LanguageSwitcher from '../features/locale/components/LanguageSwitcher';
import QueueTvPage from '../features/queue-tv/components/QueueTvPage';
import { useQueueTvController } from '../features/queue-tv/hooks/useQueueTvController';

type Props = { locale: Locale; onLocaleChange: (l: Locale) => void };

/** Composition only: the waiting-room TV (P-05). Outside the staff app: no staff session, no /v1/me/context, no navigation. */
export default function QueueTvRoute({ locale, onLocaleChange }: Props) {
  const { online } = useConnectivityController();
  return <QueueTvPage {...useQueueTvController(online)} languageSwitcher={<LanguageSwitcher inline value={locale} onChange={onLocaleChange} />} />;
}
