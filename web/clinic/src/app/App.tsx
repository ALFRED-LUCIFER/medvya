import type { Locale } from '@medvya/i18n';
import { useI18n } from '@medvya/i18n';
import { lazy, Suspense } from 'react';
import { Navigate, Outlet, Route, Routes, useNavigate } from 'react-router';
import UiGallery from '../dev/UiGallery';
import OfflineBanner from '../features/connectivity/components/OfflineBanner';
import { useConnectivityController } from '../features/connectivity/hooks/useConnectivityController';
import NotFoundPage from '../features/errors/components/NotFoundPage';
import HomePage from '../features/home/components/HomePage';
import LanguageSwitcher from '../features/locale/components/LanguageSwitcher';
import SessionPanel from '../features/session/components/SessionPanel';
import AppShell from '../features/shell/components/AppShell';
import ChooseFacilityPage from '../features/shell/components/ChooseFacilityPage';
import MorePage from '../features/shell/components/MorePage';
import ShellMessage, { ComingSoon } from '../features/shell/components/ShellMessage';
import { allows, patientDirectory } from '../features/shell/domain/navigation';
import { useShellController } from '../features/shell/hooks/useShellController';
import FrontDeskRoute from './FrontDeskRoute';
import ScheduleRoute from './ScheduleRoute';

// Patient screens load on first use, keeping them out of the first-paint bundle.
const PatientDirectoryRoute = lazy(() => import('./PatientRoutes').then(m => ({ default: m.PatientDirectoryRoute })));
const PatientRecordRoute = lazy(() => import('./PatientRoutes').then(m => ({ default: m.PatientRecordRoute })));

type Props = { locale: Locale; onLocaleChange: (l: Locale) => void };

// Composition only: controllers feed presentational components; routes exist only for screens this membership may see.
export default function App({ locale, onLocaleChange }: Props) {
  const { t } = useI18n();
  const navigate = useNavigate();
  const { online, retry } = useConnectivityController();
  const shell = useShellController();
  const switcher = <LanguageSwitcher value={locale} onChange={onLocaleChange} />;
  const account = { displayName: shell.displayName ?? '', languageSwitcher: switcher, signingOut: shell.signingOut, onSignOut: shell.signOut };
  const toHome = () => navigate('/');
  const active = shell.session?.active;
  const first = shell.items[0];
  const searchPath = allows(active ?? null, patientDirectory) ? patientDirectory.path : null;
  const registerPath = shell.items.find(i => i.id === 'register')?.path ?? null;

  const routes = shell.loading ? <Route path="*" element={<ShellMessage standalone busy title={t('shell.loading')} />} />
    : !shell.session ? <>
        <Route path="/" element={<HomePage languageSwitcher={switcher} session={<SessionPanel {...shell} onSignIn={shell.signIn} onSignOut={shell.signOut} />} />} />
        <Route path="*" element={<NotFoundPage standalone onBackToToday={toHome} />} />
      </>
    : !active ? <>
        <Route path="/choose-facility" element={<ChooseFacilityPage {...account} />} />
        <Route path="*" element={<Navigate to="/choose-facility" replace />} />
      </>
    : <>
        <Route path="/choose-facility" element={<ChooseFacilityPage {...account} />} />
        <Route element={
          <AppShell {...account} role={active.role} facilityName={active.facilityName} groups={shell.groups} tabs={shell.tabs} searchPath={searchPath}
            languageSwitcher={<LanguageSwitcher inline value={locale} onChange={onLocaleChange} />}>
            <Suspense fallback={<ShellMessage busy title={t('shell.loading')} />}><Outlet /></Suspense>
          </AppShell>}>
          <Route index element={first ? <Navigate to={first.path} replace /> : <ShellMessage title={t('shell.noScreens.title')} body={t('shell.noScreens.body')} />} />
          {shell.items.map(i => <Route key={i.id} path={i.path} element={i.id === 'schedule'
            ? <ScheduleRoute facilityId={active.facilityId} canBook={active.capabilities.RegisterBookPatients === 'Full'} />
            : i.id === 'frontDesk'
            ? <FrontDeskRoute facilityId={active.facilityId} online={online} canMove={active.capabilities.CheckInQueue === 'Full'}
                registerPath={registerPath} billingPath={shell.items.find(b => b.id === 'billing')?.path ?? null} />
            : <ComingSoon title={t(`nav.${i.id}`)} phase={i.phase} />} />)}
          {searchPath && <>
            <Route path={searchPath} element={<PatientDirectoryRoute registerPath={registerPath} />} />
            <Route path={`${searchPath}/:patientId`} element={<PatientRecordRoute />} />
          </>}
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
