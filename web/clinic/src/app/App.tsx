import type { Locale } from '@medvya/i18n';
import { useI18n } from '@medvya/i18n';
import { Navigate, Outlet, Route, Routes, useLocation, useNavigate } from 'react-router';
import UiGallery from '../dev/UiGallery';
import OfflineBanner from '../features/connectivity/components/OfflineBanner';
import { useConnectivityController } from '../features/connectivity/hooks/useConnectivityController';
import NotFoundPage from '../features/errors/components/NotFoundPage';
import HomePage from '../features/home/components/HomePage';
import LanguageSwitcher from '../features/locale/components/LanguageSwitcher';
import FacilityPicker from '../features/session/components/FacilityPicker';
import SessionExpiredPage from '../features/session/components/SessionExpiredPage';
import SessionPanel from '../features/session/components/SessionPanel';
import AppShell from '../features/shell/components/AppShell';
import MorePage from '../features/shell/components/MorePage';
import ShellMessage, { ComingSoon } from '../features/shell/components/ShellMessage';
import { useShellController } from '../features/shell/hooks/useShellController';
import ScheduleRoute from './ScheduleRoute';

type Props = { locale: Locale; onLocaleChange: (l: Locale) => void };

// Composition only: controllers feed presentational components; routes exist only for screens this membership may see.
export default function App({ locale, onLocaleChange }: Props) {
  const { t } = useI18n();
  const navigate = useNavigate();
  const location = useLocation();
  const { online, retry } = useConnectivityController();
  const shell = useShellController();
  const switcher = <LanguageSwitcher value={locale} onChange={onLocaleChange} />;
  const account = { displayName: shell.displayName ?? '', languageSwitcher: switcher, signingOut: shell.signingOut, onSignOut: shell.signOut,
    canSwitchFacility: (shell.session?.memberships.length ?? 0) > 1 };
  const toHome = () => navigate('/');
  const active = shell.session?.active;
  const first = shell.items[0];
  // After choosing, continue to the page that sent the user to the picker (F-05a: back to the previous route after re-sign-in).
  const from = (location.state as { from?: string } | null)?.from;
  const picker = (
    <FacilityPicker {...account} memberships={shell.session?.memberships ?? []} choosingId={shell.choosingId} failed={shell.chooseFailed}
      onChoose={id => shell.chooseFacility(id, () => navigate(from ?? '/', { replace: true }))} onCancel={active ? () => navigate(-1) : undefined} />
  );

  // F-05a: an ended session replaces every screen; signing in again comes back to this route.
  const routes = shell.loading ? <Route path="*" element={<ShellMessage standalone busy title={t('shell.loading')} />} />
    : shell.expired ? <Route path="*" element={<SessionExpiredPage onSignIn={shell.signIn} />} />
    : !shell.session ? <>
        <Route path="/" element={<HomePage languageSwitcher={switcher} session={<SessionPanel {...shell} onSignIn={shell.signIn} onSignOut={shell.signOut} />} />} />
        <Route path="*" element={<NotFoundPage standalone onBackToToday={toHome} />} />
      </>
    : !active ? <>
        <Route path="/choose-facility" element={picker} />
        <Route path="*" element={<Navigate to="/choose-facility" replace state={{ from: location.pathname }} />} />
      </>
    : <>
        <Route path="/choose-facility" element={picker} />
        <Route element={
          <AppShell {...account} role={active.role} facilityName={active.facilityName} groups={shell.groups} tabs={shell.tabs}
            languageSwitcher={<LanguageSwitcher inline value={locale} onChange={onLocaleChange} />}>
            <Outlet />
          </AppShell>}>
          <Route index element={first ? <Navigate to={first.path} replace /> : <ShellMessage title={t('shell.noScreens.title')} body={t('shell.noScreens.body')} />} />
          {shell.items.map(i => <Route key={i.id} path={i.path} element={i.id === 'schedule'
            ? <ScheduleRoute facilityId={active.facilityId} online={online} />
            : <ComingSoon title={t(`nav.${i.id}`)} phase={i.phase} />} />)}
          <Route path="/more" element={<MorePage {...account} role={active.role} facilityName={active.facilityName} groups={shell.groups} />} />
          <Route path="*" element={<NotFoundPage onBackToToday={toHome} />} />
        </Route>
      </>;

  return (
    <>
      {!online && <OfflineBanner onRetry={retry} />}
      <Routes>
        <Route path="/__ui" element={<UiGallery switcher={switcher} />} />
        {routes}
      </Routes>
    </>
  );
}
