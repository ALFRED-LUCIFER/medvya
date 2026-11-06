import { lazy, Suspense } from 'react';
import type { Locale } from '@medvya/i18n';
import { I18nProvider, useI18n } from '@medvya/i18n';
import { Navigate, Outlet, Route, Routes, useLocation, useNavigate } from 'react-router';
import UiGallery from '../dev/UiGallery';
import OfflineBanner from '../features/connectivity/components/OfflineBanner';
import { useConnectivityController } from '../features/connectivity/hooks/useConnectivityController';
import NotFoundPage from '../features/errors/components/NotFoundPage';
import TermsNotice from '../features/settings/components/TermsNotice';
import { useTermsNotice } from '../features/settings/hooks/useTermsNotice';
import LicenceNotice from '../features/onboarding/components/LicenceNotice';
import HomePage from '../features/home/components/HomePage';
import LanguageSwitcher from '../features/locale/components/LanguageSwitcher';
import FacilityPicker from '../features/session/components/FacilityPicker';
import SessionExpiredPage from '../features/session/components/SessionExpiredPage';
import SessionPanel from '../features/session/components/SessionPanel';
import AppShell from '../features/shell/components/AppShell';
import MorePage from '../features/shell/components/MorePage';
import ShellMessage, { ComingSoon } from '../features/shell/components/ShellMessage';
import { allows, patientDirectory } from '../features/shell/domain/navigation';
import { useShellController } from '../features/shell/hooks/useShellController';
import FrontDeskRoute from './FrontDeskRoute';
import NotificationsRoute from './NotificationsRoute';
import ScheduleRoute from './ScheduleRoute';

// Loaded on demand: invitations bring the form stack (React Hook Form, Zod) that most sessions never need.
const InvitationRoute = lazy(() => import('./InvitationRoute'));
const InviteColleagueRoute = lazy(() => import('./InviteColleagueRoute'));
const SignUpRoute = lazy(() => import('./SignUpRoute'));
const OnboardingRoute = lazy(() => import('./OnboardingRoute'));
const DisplaysRoute = lazy(() => import('./DisplaysRoute'));
const OverviewRoute = lazy(() => import('./OverviewRoute'));
const SettingsRoute = lazy(() => import('./SettingsRoute'));
// Patient screens load on first use, keeping them out of the first-paint bundle.
const PatientDirectoryRoute = lazy(() => import('./PatientRoutes').then(m => ({ default: m.PatientDirectoryRoute })));
const PatientRecordRoute = lazy(() => import('./PatientRoutes').then(m => ({ default: m.PatientRecordRoute })));
const MergeReviewRoute = lazy(() => import('./PatientRoutes').then(m => ({ default: m.MergeReviewRoute })));
const RegistrationRoute = lazy(() => import('./PatientRoutes').then(m => ({ default: m.RegistrationRoute })));
// Walk-in (MDV-P1-025) brings the patient picker and booking lists: loaded when opened, never at first paint.
const WalkInRoute = lazy(() => import('./WalkInRoute'));
const AdjustmentsRoute = lazy(() => import('./AdjustmentsRoute'));
const CompleteRegistrationRoute = lazy(() => import('./PatientRoutes').then(m => ({ default: m.CompleteRegistrationRoute })));

type Props = { locale: Locale; onLocaleChange: (l: Locale) => void };

// Composition only: controllers feed presentational components; routes exist only for screens this membership may see.
export default function App({ locale, onLocaleChange }: Props) {
  const { t, country: signedOutCountry } = useI18n();
  const navigate = useNavigate();
  const location = useLocation();
  const { online, retry } = useConnectivityController();
  const shell = useShellController();
  const termsPending = useTermsNotice(shell.items.some(i => i.id === 'settings'));
  const switcher = <LanguageSwitcher value={locale} onChange={onLocaleChange} />;
  const account = { displayName: shell.displayName ?? '', languageSwitcher: switcher, signingOut: shell.signingOut, onSignOut: shell.signOut,
    canSwitchFacility: (shell.session?.memberships.length ?? 0) > 1 };
  const toHome = () => navigate('/');
  const active = shell.session?.active;
  // Signed in at a facility, rules and formats follow the tenant's country (from the server, never the client).
  const country = active?.country ?? signedOutCountry;
  const first = shell.items[0];
  // After choosing, continue to the page that sent the user to the picker (F-05a: back to the previous route after re-sign-in).
  const from = (location.state as { from?: string } | null)?.from;
  const picker = (
    <FacilityPicker {...account} memberships={shell.session?.memberships ?? []} choosingId={shell.choosingId} failed={shell.chooseFailed}
      onChoose={id => shell.chooseFacility(id, () => navigate(from ?? '/', { replace: true }))} onCancel={active ? () => navigate(-1) : undefined} />
  );
  const searchPath = allows(active ?? null, patientDirectory) ? patientDirectory.path : null;
  const registerPath = shell.items.find(i => i.id === 'register')?.path ?? null;
  const pathOf = (id: string) => shell.items.find(i => i.id === id)?.path ?? null;
  // Walk-in (MDV-P1-025) books and checks in: Register / book patients and Check-in & queue, both at Full.
  const walkInPath = pathOf('frontDesk') && active?.capabilities.CheckInQueue === 'Full' && active.capabilities.RegisterBookPatients === 'Full'
    ? `${pathOf('frontDesk')}/walk-in` : null;
  // Leave and schedule exceptions (MDV-P1-012) change who can be booked: Register / book patients at Full.
  const leavePath = pathOf('schedule') && active?.capabilities.RegisterBookPatients === 'Full' ? `${pathOf('schedule')}/leave` : null;
  const registration = <RegistrationRoute country={country} actorName={shell.displayName ?? ''} timeZone={active?.timeZone} bookPath={pathOf('schedule')} checkInPath={walkInPath} />;

  // F-05a: an ended session replaces every screen; signing in again comes back to this route.
  const routes = shell.loading ? <Route path="*" element={<ShellMessage standalone busy title={t('shell.loading')} />} />
    : shell.expired ? <Route path="*" element={<SessionExpiredPage onSignIn={shell.signIn} />} />
    : !shell.session ? <>
        <Route path="/" element={<HomePage languageSwitcher={switcher} session={<SessionPanel {...shell} onSignIn={shell.signIn} onSignOut={shell.signOut} />} />} />
        <Route path="/signup" element={<SignUpRoute switcher={switcher} />} />
        {/* A signed-out visit to the wizard (an old link) starts at sign-up. */}
        <Route path="/onboarding" element={<Navigate to={`/signup${location.search}`} replace />} />
        <Route path="*" element={<NotFoundPage standalone onBackToToday={toHome} />} />
      </>
    : !active ? <>
        <Route path="/choose-facility" element={picker} />
        {/* Signed in with no clinic yet: the setup wizard (F-04). It scopes the session to the new practice when it finishes. */}
        <Route path="/onboarding" element={<OnboardingRoute switcher={switcher} onFinished={r => shell.chooseFacility(r.membershipId, () => navigate('/', { replace: true }))} />} />
        <Route path="*" element={<Navigate to={shell.session.memberships.length === 0 ? '/onboarding' : '/choose-facility'} replace state={{ from: location.pathname }} />} />
      </>
    : <>
        <Route path="/choose-facility" element={picker} />
        <Route element={
          <AppShell {...account} role={active.role} facilityName={active.facilityName} groups={shell.groups} tabs={shell.tabs} searchPath={searchPath}
            notifications={allows(active, patientDirectory) ? <NotificationsRoute facilityId={active.facilityId} online={online} /> : undefined}
            languageSwitcher={<LanguageSwitcher inline value={locale} onChange={onLocaleChange} />}>
            {active.licencePending && <LicenceNotice />}
            {termsPending && <TermsNotice />}
            <Suspense fallback={<ShellMessage busy title={t('shell.loading')} />}><Outlet /></Suspense>
          </AppShell>}>
          <Route index element={first ? <Navigate to={first.path} replace /> : <ShellMessage title={t('shell.noScreens.title')} body={t('shell.noScreens.body')} />} />
          {shell.items.map(i => <Route key={i.id} path={i.path} element={i.id === 'schedule'
            ? <ScheduleRoute facilityId={active.facilityId} userId={shell.session?.user.id ?? ''} userName={shell.session?.user.displayName ?? ''} canBook={active.capabilities.RegisterBookPatients === 'Full'}
                canCheckIn={active.capabilities.CheckInQueue === 'Full'} registerPath={registerPath} leavePath={leavePath} />
            : i.id === 'register' ? registration
            : i.id === 'frontDesk'
            ? <FrontDeskRoute facilityId={active.facilityId} online={online} canMove={active.capabilities.CheckInQueue === 'Full'}
                registerPath={registerPath} walkInPath={walkInPath} billingPath={shell.items.find(b => b.id === 'billing')?.path ?? null} />
            : i.id === 'settings' ? <SettingsRoute facilityId={active.facilityId} />
            // Until the patient chart exists (phase 2) the Patients menu opens the patient directory instead of a placeholder.
            : i.id === 'patients' && searchPath ? <Navigate to={searchPath} replace />
            : <ComingSoon title={t(`nav.${i.id}`)} phase={i.phase} />} />)}
          {leavePath && <Route path={leavePath} element={<AdjustmentsRoute facilityId={active.facilityId} schedulePath={pathOf('schedule')!} boardPath={pathOf('frontDesk')} />} />}
          {walkInPath && registerPath &&
            <Route path={walkInPath} element={<WalkInRoute facilityId={active.facilityId} boardPath={pathOf('frontDesk')!} registerPath={`${registerPath}?mode=quick`} />} />}
          {/* shortcut: reachable by URL only; the Staff screen and its navigation entry come with F-10 (MDV-P1-008). */}
          {active.capabilities.StaffRolesIntegrations === 'Config' && <Route path="/admin/staff" element={<InviteColleagueRoute />} />}
          {/* D-04 clinic overview (MDV-P1-030): for everyone who sees the board; moving patients needs the full queue capability. */}
          {pathOf('frontDesk') && <Route path="/front-desk/overview" element={
            <OverviewRoute facilityId={active.facilityId} online={online} canMove={active.capabilities.CheckInQueue === 'Full'}
              userId={shell.session?.user.id ?? ''} boardPath={pathOf('frontDesk')!} />} />}
          {/* shortcut: reachable by URL only, like Staff; it moves to device management in Settings with MDV-P0-012. */}
          {active.capabilities.StaffRolesIntegrations === 'Config' && <Route path="/admin/displays" element={<DisplaysRoute facilityId={active.facilityId} />} />}
          {searchPath && <>
            <Route path={searchPath} element={<PatientDirectoryRoute registerPath={registerPath} mergePath={`${searchPath}/merge`} />} />
            <Route path={`${searchPath}/merge/:requestId?`} element={<MergeReviewRoute can={{ request: active.capabilities.RegisterBookPatients === 'Full',
              approve: active.capabilities.ClinicalNotes === 'Sign', undo: active.capabilities.StaffRolesIntegrations === 'Config' }} />} />
            <Route path={`${searchPath}/:patientId`} element={<PatientRecordRoute canReveal={active.capabilities.RegisterBookPatients === 'Full'} />} />
            {active.capabilities.RegisterBookPatients === 'Full' &&
              <Route path={`${searchPath}/:patientId/complete`} element={<CompleteRegistrationRoute country={country} />} />}
          </>}
          <Route path="/more" element={<MorePage {...account} role={active.role} facilityName={active.facilityName} groups={shell.groups} />} />
          <Route path="*" element={<NotFoundPage onBackToToday={toHome} />} />
        </Route>
      </>;

  return (
    <I18nProvider locale={locale} country={country}>
      {!online && <OfflineBanner onRetry={retry} />}
      <Suspense fallback={<ShellMessage standalone busy title={t('shell.loading')} />}>
        <Routes>
          <Route path="/__ui" element={<UiGallery switcher={switcher} />} />
          {/* Public and token-based (F-06): the same page whether or not someone is signed in. */}
          <Route path="/invite/:token" element={<InvitationRoute switcher={switcher} />} />
          {routes}
        </Routes>
      </Suspense>
    </I18nProvider>
  );
}
