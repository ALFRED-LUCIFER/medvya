import type { Locale } from '@medvya/i18n';
import UiGallery from '../dev/UiGallery';
import OfflineBanner from '../features/connectivity/components/OfflineBanner';
import { useConnectivityController } from '../features/connectivity/hooks/useConnectivityController';
import NotFoundPage from '../features/errors/components/NotFoundPage';
import HomePage from '../features/home/components/HomePage';
import LanguageSwitcher from '../features/locale/components/LanguageSwitcher';
import SessionPanel from '../features/session/components/SessionPanel';
import { useSessionController } from '../features/session/hooks/useSessionController';

type Props = { locale: Locale; onLocaleChange: (l: Locale) => void };

// Composition only: controllers feed presentational components.
// shortcut: path check instead of a router; React Router arrives with the app shell (MDV-P0-022).
export default function App({ locale, onLocaleChange }: Props) {
  const { online, retry } = useConnectivityController();
  const session = useSessionController();
  const switcher = <LanguageSwitcher value={locale} onChange={onLocaleChange} />;
  const path = window.location.pathname;
  const page = path === '/__ui' ? <UiGallery switcher={switcher} />
    : path === '/' ? <HomePage languageSwitcher={switcher} session={<SessionPanel {...session} onSignIn={session.signIn} onSignOut={session.signOut} />} />
    : <NotFoundPage onBackToToday={() => window.location.assign('/')} />;
  return <>{!online && <OfflineBanner onRetry={retry} />}{page}</>;
}
