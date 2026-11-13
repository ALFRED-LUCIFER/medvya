import { lazy, Suspense, useEffect, useRef } from 'react';
import type { Locale } from '@medvya/i18n';
import { I18nProvider, useI18n } from '@medvya/i18n';
import { SystemState } from '@medvya/ui';
import { Navigate, Outlet, Route, Routes, useLocation, useNavigate } from 'react-router';
import UiGallery from '../dev/UiGallery';
import OfflineBanner from '../features/connectivity/components/OfflineBanner';
import { useConnectivityController } from '../features/connectivity/hooks/useConnectivityController';
import LoadErrorBoundary from '../features/errors/components/LoadErrorBoundary';
import NotFoundPage from '../features/errors/components/NotFoundPage';
import TermsNotice from '../features/settings/components/TermsNotice';
import { useTermsNotice } from '../features/settings/hooks/useTermsNotice';
import OrganisationNotice from '../features/onboarding/components/OrganisationNotice';
import { organisationPath } from '../features/onboarding/domain/organisationRequest';
import LanguageSwitcher from '../features/locale/components/LanguageSwitcher';
import FacilityPicker from '../features/session/components/FacilityPicker';
import SessionExpiredPage from '../features/session/components/SessionExpiredPage';
import AppShell from '../features/shell/components/AppShell';
import MorePage from '../features/shell/components/MorePage';
import ShellMessage, { ComingSoon } from '../features/shell/components/ShellMessage';
import { allows, homeItem, patientDirectory } from '../features/shell/domain/navigation';
import { useShellController } from '../features/shell/hooks/useShellController';
import FrontDeskRoute from './FrontDeskRoute';
import GlobalSearchRoute from './GlobalSearchRoute';
import NotificationsRoute from './NotificationsRoute';
import { NurseIntakeRoute, NurseQueueRoute } from './NurseRoutes';
import ScheduleRoute from './ScheduleRoute';

// Loaded on demand: invitations bring the form stack (React Hook Form, Zod) that most sessions never need.
const InvitationRoute = lazy(() => import('./InvitationRoute'));
const RxVerifyRoute = lazy(() => import('./RxVerifyRoute'));
const SharedRxRoute = lazy(() => import('./RxVerifyRoute').then(m => ({ default: m.SharedRxRoute })));
const PaperVerifyRoute = lazy(() => import('./RxVerifyRoute').then(m => ({ default: m.PaperVerifyRoute })));
const InviteColleagueRoute = lazy(() => import('./InviteColleagueRoute'));
const SignUpRoute = lazy(() => import('./SignUpRoute'));
const SignInRoute = lazy(() => import('./SignInRoute'));
const OnboardingRoute = lazy(() => import('./OnboardingRoute'));
const OverviewRoute = lazy(() => import('./OverviewRoute'));
const SettingsRoute = lazy(() => import('./SettingsRoute'));
const MyDevicesRoute = lazy(() => import('./MyDevicesRoute'));
const devicesPath = '/account/devices';
// Patient screens load on first use, keeping them out of the first-paint bundle.
const PatientDirectoryRoute = lazy(() => import('./PatientRoutes').then(m => ({ default: m.PatientDirectoryRoute })));
const PatientRecordRoute = lazy(() => import('./PatientRoutes').then(m => ({ default: m.PatientRecordRoute })));
const MergeReviewRoute = lazy(() => import('./PatientRoutes').then(m => ({ default: m.MergeReviewRoute })));
const RegistrationRoute = lazy(() => import('./PatientRoutes').then(m => ({ default: m.RegistrationRoute })));
// Walk-in (MDV-P1-025) brings the patient picker and booking lists: loaded when opened, never at first paint.
const WalkInRoute = lazy(() => import('./WalkInRoute'));
const WaitlistRoute = lazy(() => import('./WaitlistRoute'));
const TodayRoute = lazy(() => import('./TodayRoute'));
const ConsultRoute = lazy(() => import('./ConsultRoute'));
const LetterRoute = lazy(() => import('./ConsultRoute').then(m => ({ default: m.LetterRoute })));
const SlipRoute = lazy(() => import('./ConsultRoute').then(m => ({ default: m.SlipRoute })));
const NotePrintRoute = lazy(() => import('./ConsultRoute').then(m => ({ default: m.NotePrintRoute })));
const RxPrintRoute = lazy(() => import('./ConsultRoute').then(m => ({ default: m.RxPrintRoute })));
const ReconciliationRoute = lazy(() => import('./ReconciliationRoute'));
const ProblemsRoute = lazy(() => import('./ProblemsRoute'));
const Patient360Route = lazy(() => import('./Patient360Route'));
const TimelineRoute = lazy(() => import('./TimelineRoute'));
const DocumentsRoute = lazy(() => import('./DocumentsRoute'));
const AdjustmentsRoute = lazy(() => import('./AdjustmentsRoute'));
const OrganisationRoute = lazy(() => import('./OrganisationRoute'));
const CompleteRegistrationRoute = lazy(() => import('./PatientRoutes').then(m => ({ default: m.CompleteRegistrationRoute })));

type Props = { locale: Locale; onLocaleChange: (l: Locale) => void };

// Composition only: controllers feed presentational components; routes exist only for screens this membership may see.
export default function App({ locale, onLocaleChange }: Props) {
  const { t, country: signedOutCountry } = useI18n();
  const navigate = useNavigate();
  const location = useLocation();
  // A link that took the doctor to another screen is gone, so focus would fall to the page start: move it to the content, which a screen reader then reads.
  const path = useRef(location.pathname);
  useEffect(() => {
    if (path.current === location.pathname) return;
    path.current = location.pathname;
    if (document.activeElement === document.body) document.getElementById('main')?.focus();
  }, [location.pathname]);
  const { online, retry, reload } = useConnectivityController();
  const shell = useShellController();
  const termsPending = useTermsNotice(shell.items.some(i => i.id === 'settings'));
  const switcher = <LanguageSwitcher value={locale} onChange={onLocaleChange} />;
  const account = { displayName: shell.displayName ?? '', languageSwitcher: switcher, signingOut: shell.signingOut, onSignOut: () => shell.signOut(() => navigate('/', { replace: true })),
    canSwitchFacility: (shell.session?.memberships.length ?? 0) > 1 };
  const toHome = () => navigate('/');
  const active = shell.session?.active;
  // Signed in at a facility, rules and formats follow the tenant's country (from the server, never the client).
  const country = active?.country ?? signedOutCountry;
  const first = homeItem(shell.items, active ?? null);
  // After choosing, continue to the page that sent the user to the picker (F-05a: back to the previous route after re-sign-in).
  const from = (location.state as { from?: string } | null)?.from;
  const picker = (
    <FacilityPicker {...account} memberships={shell.session?.memberships ?? []} lastMembershipId={shell.session?.lastMembershipId ?? null} choosingId={shell.choosingId} failed={shell.chooseFailed}
      onChoose={id => shell.chooseFacility(id, () => navigate(from ?? '/', { replace: true }))} onCancel={active ? () => navigate(-1) : undefined} />
  );
  const searchPath = allows(active ?? null, patientDirectory) ? patientDirectory.path : null;
  // The front desk's part of documents: register / book and check-in both at Full, as the server's rule (DocumentRules.CanUploadAtDesk).
  const deskDocuments = active?.capabilities.RegisterBookPatients === 'Full' && active.capabilities.CheckInQueue === 'Full';
  const registerPath = shell.items.find(i => i.id === 'register')?.path ?? null;
  const pathOf = (id: string) => shell.items.find(i => i.id === id)?.path ?? null;
  const nursePath = pathOf('nurse');
  // Walk-in (MDV-P1-025) books and checks in: Register / book patients and Check-in & queue, both at Full.
  const walkInPath = pathOf('frontDesk') && active?.capabilities.CheckInQueue === 'Full' && active.capabilities.RegisterBookPatients === 'Full'
    ? `${pathOf('frontDesk')}/walk-in` : null;
  // The waitlist (MDV-P1-027) holds slots and books them: Register / book patients at Full.
  const waitlistPath = pathOf('frontDesk') && active?.capabilities.RegisterBookPatients === 'Full' ? `${pathOf('frontDesk')}/waitlist` : null;
  // Leave and schedule exceptions (MDV-P1-012) change who can be booked: Register / book patients at Full.
  const leavePath = pathOf('schedule') && active?.capabilities.RegisterBookPatients === 'Full' ? `${pathOf('schedule')}/leave` : null;
  const registration = <RegistrationRoute country={country} scope={active?.facilityId ?? ''} actorName={shell.displayName ?? ''} timeZone={active?.timeZone} bookPath={pathOf('schedule')} checkInPath={walkInPath} />;

  // F-05a: an ended session replaces every screen; signing in again comes back to this route.
  const routes = shell.loading ? <Route path="*" element={<ShellMessage standalone busy title={t('shell.loading')} />} />
    : shell.expired ? <Route path="*" element={<SessionExpiredPage languageSwitcher={switcher} onSignIn={shell.signIn}
        onOtherAccount={() => { shell.forgetEndedSession(); navigate('/', { replace: true }); }} />} />
    : !shell.session ? <>
        <Route path="/" element={<SignInRoute switcher={switcher} />} />
        <Route path="/signup" element={<SignUpRoute switcher={switcher} />} />
        {/* A signed-out visit to the wizard (an old link) starts at sign-up. */}
        <Route path="/onboarding" element={<Navigate to={`/signup${location.search}`} replace />} />
        {/* Signed out (offboarded, cookie gone, a shared link): sign in here, and the provider returns to this address. */}
        <Route path="*" element={<SignInRoute switcher={switcher} />} />
      </>
    : !active ? <>
        <Route path="/choose-facility" element={shell.choosingOnly ? <ShellMessage standalone busy title={t('shell.loading')} /> : picker} />
        {/* Signed in with no clinic yet: the setup wizard (F-04). It scopes the session to the new practice when it finishes. */}
        <Route path="/onboarding" element={<OnboardingRoute switcher={switcher} onLater={() => shell.signOut()}
          onFinished={(r, to = '/') => shell.chooseFacility(r.membershipId, () => navigate(to, { replace: true }))} />} />
        <Route path="*" element={<Navigate to={shell.session.memberships.length === 0 ? '/onboarding' : '/choose-facility'} replace state={{ from: location.pathname }} />} />
      </>
    : <>
        {/* With one clinic there is no other to switch to: the picker (also reached right after the automatic choice) goes on to the screen asked for. */}
        <Route path="/choose-facility" element={shell.session.memberships.length > 1 ? picker : <Navigate to={from ?? '/'} replace />} />
        <Route element={
          <AppShell {...account} devicesPath={devicesPath} role={active.role} facilityName={active.facilityName} groups={shell.groups} tabs={shell.tabs} searchPath={searchPath}
            globalSearch={searchPath && ['Read', 'Limited', 'Full', 'Sign'].includes(active.capabilities.Patient360Chart ?? 'None')
              ? <GlobalSearchRoute startVisitPath={walkInPath} bookPath={pathOf('schedule')} registerPath={registerPath} /> : undefined}
            notifications={allows(active, patientDirectory) || allows(active, { capability: 'StaffRolesIntegrations', access: ['Config'] } as const) ? <NotificationsRoute facilityId={active.facilityId} online={online} /> : undefined}
            languageSwitcher={<LanguageSwitcher inline value={locale} onChange={onLocaleChange} />}>
            <OrganisationNotice state={active.organisationState} requestPath={active.capabilities.StaffRolesIntegrations === 'Config' ? organisationPath : undefined} />
            {termsPending && <TermsNotice />}
            <Suspense fallback={<ShellMessage busy title={t('shell.loading')} />}><Outlet /></Suspense>
          </AppShell>}>
          <Route index element={first ? <Navigate to={first.path} replace /> : <ShellMessage title={t('shell.noScreens.title')} body={t('shell.noScreens.body')} />} />
          {shell.items.map(i => <Route key={i.id} path={i.path} element={i.id === 'schedule'
            ? <ScheduleRoute facilityId={active.facilityId} userId={shell.session?.user.id ?? ''} userName={shell.session?.user.displayName ?? ''} canBook={active.capabilities.RegisterBookPatients === 'Full'}
                canCheckIn={active.capabilities.CheckInQueue === 'Full'} registerPath={registerPath} leavePath={leavePath} />
            : i.id === 'register' ? registration
            // Doctors (Patient 360 at Full) land on their own Today (MDV-P2-002); reception and nurses keep the board there.
            : i.id === 'today' && active.capabilities.Patient360Chart === 'Full' && pathOf('frontDesk')
            ? <TodayRoute facilityId={active.facilityId} online={online} userId={shell.session?.user.id ?? ''} />
            : i.id === 'frontDesk' || (i.id === 'today' && pathOf('frontDesk'))
            ? <FrontDeskRoute facilityId={active.facilityId} online={online} canMove={active.capabilities.CheckInQueue === 'Full'}
                registerPath={registerPath} walkInPath={walkInPath} waitlistPath={waitlistPath} billingPath={shell.items.find(b => b.id === 'billing')?.path ?? null}
                canConsult={active.capabilities.Patient360Chart === 'Full'} steps={{ nurse: active.flags.includes('clinical.nurse-intake'), billing: active.flags.includes('billing.invoices') }}
                intakePath={nursePath && active.capabilities.VitalsAllergiesEntry === 'Full' ? (id: string) => `${nursePath}/${id}` : null} />
            : i.id === 'nurse' ? <NurseQueueRoute facilityId={active.facilityId} online={online} nursePath={i.path} />
            : i.id === 'settings' ? <SettingsRoute facilityId={active.facilityId} country={active.country ?? null} />
            // Until the patient chart exists (phase 2) the Patients menu opens the patient directory instead of a placeholder.
            : i.id === 'patients' && searchPath ? <Navigate to={searchPath} replace />
            : <ComingSoon title={t(`nav.${i.id}`)} phase={i.phase} />} />)}
          {leavePath && <Route path={leavePath} element={<AdjustmentsRoute facilityId={active.facilityId} canConfigure={active.capabilities.StaffRolesIntegrations === 'Config'} userId={shell.session?.user.id ?? ''} schedulePath={pathOf('schedule')!} boardPath={pathOf('frontDesk')} />} />}
          {walkInPath && registerPath &&
            <Route path={walkInPath} element={<WalkInRoute facilityId={active.facilityId} boardPath={pathOf('frontDesk')!} registerPath={`${registerPath}?mode=quick`} />} />}
          {waitlistPath && <Route path={waitlistPath} element={<WaitlistRoute facilityId={active.facilityId} boardPath={pathOf('frontDesk')!} />} />}
          {/* shortcut: reachable by URL only; the Staff screen and its navigation entry come with F-10 (MDV-P1-008). */}
          {active.capabilities.StaffRolesIntegrations === 'Config' && <Route path="/admin/staff" element={<InviteColleagueRoute />} />}
          {/* The owner's onboarding request (ADR-016, MDV-P1-062): status, documents and Submit; the shell notice links here until approval. */}
          {active.capabilities.StaffRolesIntegrations === 'Config' && <Route path={organisationPath} element={<OrganisationRoute />} />}
          {/* D-04 clinic overview (MDV-P1-030): for everyone who sees the board; moving patients needs the full queue capability. */}
          {pathOf('frontDesk') && <Route path="/front-desk/overview" element={
            <OverviewRoute facilityId={active.facilityId} online={online} canMove={active.capabilities.CheckInQueue === 'Full'}
              userId={shell.session?.user.id ?? ''} boardPath={pathOf('frontDesk')!} />} />}
          {/* Nurse intake (MDV-P2-007): one visit's intake, reached from the queue or a With nurse card; the server checks clinical access on every call. */}
          {nursePath && <Route path={`${nursePath}/:appointmentId`} element={<NurseIntakeRoute online={online} nursePath={nursePath} />} />}
          {/* A consultation (MDV-P2-001) for any role that treats patients; the server checks the care relationship on every call. */}
          {['Read', 'Limited', 'Full', 'Sign'].includes(active.capabilities.Patient360Chart ?? 'None') &&
            <Route path="/consult/:encounterId" element={<ConsultRoute boardPath={pathOf('frontDesk') ?? '/'} online={online} canOrder={active.capabilities.PrescriptionsOrders === 'Sign'} canReconcile={active.capabilities.Patient360Chart === 'Full'} canSign={active.capabilities.ClinicalNotes === 'Sign'} />} />}
          {/* The signed note's print view and PDF (MDV-P2-013): the note's readers (Patient 360 at Full); the server decides for this patient. */}
          {active.capabilities.Patient360Chart === 'Full' &&
            <Route path="/consult/:encounterId/note/print" element={<NotePrintRoute />} />}
          {/* The investigation slip (MDV-P2-018) and the printed prescription (MDV-P2-017): same roles; the server lets only an ordering doctor read them. */}
          {active.capabilities.PrescriptionsOrders === 'Sign' && <>
            <Route path="/consult/:encounterId/slip" element={<SlipRoute />} />
            <Route path="/consult/:encounterId/rx/print" element={<RxPrintRoute />} />
            <Route path="/consult/:encounterId/referrals/:referralId/letter" element={<LetterRoute />} />
          </>}
          {/* Medication reconciliation (MDV-P2-011) and diagnosis coding (MDV-P2-010), E-07: a doctor's, inside a consultation. */}
          {active.capabilities.Patient360Chart === 'Full' && <>
            <Route path="/consult/:encounterId/medicines" element={<ReconciliationRoute boardPath={pathOf('frontDesk') ?? '/'} online={online} />} />
            <Route path="/consult/:encounterId/problems" element={<ProblemsRoute boardPath={pathOf('frontDesk') ?? '/'} online={online} />} />
          </>}
          {/* Without Register patient (a nurse), /patients/new is not a patient id: say the role cannot register instead of "patient not found". */}
          {!registerPath && <Route path="/patients/new" element={<ShellMessage title={t('nav.register')} body={t('registration.forbidden.body')} />} />}
          {/* Patient 360 (MDV-P2-004, E-01): same roles; /patients/new and /patients/directory are more specific and win. */}
          {['Read', 'Limited', 'Full', 'Sign'].includes(active.capabilities.Patient360Chart ?? 'None') &&
            <>
              <Route path="/patients/:patientId" element={<Patient360Route backPath={searchPath ?? '/'} canEditHistory={active.capabilities.VitalsAllergiesEntry === 'Full'} canManagePregnancy={active.capabilities.ClinicalNotes === 'Sign'} canOrder={active.capabilities.PrescriptionsOrders === 'Sign'} online={online} userId={shell.session?.user.id ?? ''} />} />
              <Route path="/patients/:patientId/timeline" element={<TimelineRoute printRx={active.capabilities.PrescriptionsOrders === 'Sign'} />} />
            </>}
          {/* Documents (MDV-P2-006): clinicians, and the front desk (upload and list only; the server refuses it any file). */}
          {(['Read', 'Limited', 'Full', 'Sign'].includes(active.capabilities.Patient360Chart ?? 'None') || deskDocuments) &&
            <Route path="/patients/:patientId/docs" element={<DocumentsRoute clinician={['Read', 'Limited', 'Full', 'Sign'].includes(active.capabilities.Patient360Chart ?? 'None')} deskRecordPath={searchPath} />} />}
          {/* Queue displays live in Settings (MDV-P1-052); the old address still works for links already shared. */}
          <Route path="/admin/displays" element={<Navigate to="/settings?tab=displays" replace />} />
          {searchPath && <>
            <Route path={searchPath} element={<PatientDirectoryRoute registerPath={registerPath} mergePath={`${searchPath}/merge`} />} />
            <Route path={`${searchPath}/merge/:requestId?`} element={<MergeReviewRoute can={{ request: active.capabilities.RegisterBookPatients === 'Full',
              approve: active.capabilities.ClinicalNotes === 'Sign', undo: active.capabilities.StaffRolesIntegrations === 'Config' }} />} />
            <Route path={`${searchPath}/:patientId`} element={<PatientRecordRoute canReveal={active.capabilities.RegisterBookPatients === 'Full'} canDocs={deskDocuments} facilityId={active.facilityId} timeZone={active.timeZone} bookPath={pathOf('schedule')} checkInPath={walkInPath} />} />
            {active.capabilities.RegisterBookPatients === 'Full' &&
              <Route path={`${searchPath}/:patientId/complete`} element={<CompleteRegistrationRoute country={country} />} />}
          </>}
          {/* My devices (MDV-P0-012): any signed-in member; the server only ever lists and ends the caller's own sessions. */}
          <Route path={devicesPath} element={<MyDevicesRoute timeZone={active.timeZone} />} />
          <Route path="/more" element={<MorePage {...account} devicesPath={devicesPath} role={active.role} facilityName={active.facilityName} groups={shell.groups} />} />
          <Route path="*" element={<NotFoundPage onHome={toHome} />} />
        </Route>
      </>;

  return (
    <I18nProvider locale={locale} country={country}>
      {!online && <OfflineBanner onRetry={retry} />}
      {/* Offline, the banner already says why the screen is missing; back online, a reload fetches it. */}
      <LoadErrorBoundary fallback={online && <main className="grid min-h-screen place-items-center p-4"><SystemState state="offline" onAction={reload} /></main>}>
        <Suspense fallback={<ShellMessage standalone busy title={t('shell.loading')} />}>
          <Routes>
            <Route path="/__ui" element={<UiGallery switcher={switcher} />} />
            {/* Public and token-based (F-06): the same page whether or not someone is signed in. */}
            <Route path="/invite/:token" element={<InvitationRoute switcher={switcher} />} />
            {/* The QR code on a printed prescription (MDV-P2-017): public, validity only. */}
            <Route path="/verify/rx/:token" element={<RxVerifyRoute switcher={switcher} />} />
            {/* The QR code on an investigation slip or referral letter (MDV-P2-018/019): public, what the paper is, never whose. */}
            <Route path="/verify/paper/:token" element={<PaperVerifyRoute switcher={switcher} />} />
            {/* A shared prescription link (MDV-P2-020): public, it opens with the code sent to the patient's mobile. */}
            <Route path="/rx/:token" element={<SharedRxRoute switcher={switcher} />} />
            {routes}
          </Routes>
        </Suspense>
      </LoadErrorBoundary>
    </I18nProvider>
  );
}
