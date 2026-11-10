import { useI18n, type Locale } from '@medvya/i18n';
import { Route, Routes } from 'react-router';
import LanguageSwitcher from '../features/locale/components/LanguageSwitcher';
import SignInPage from '../features/session/components/SignInPage';
import { useSessionController } from '../features/session/hooks/useSessionController';
import ConsoleShell from '../features/shell/components/ConsoleShell';
import { navFor } from '../features/shell/domain/navigation';
import OverviewPage from '../features/overview/components/OverviewPage';
import { AuditRoute, OrganisationDetailRoute, OrganisationQueueRoute, TeamRoute } from './routes';

export default function App({ locale, onLocaleChange }: { locale: Locale; onLocaleChange: (l: Locale) => void }) {
  const { t } = useI18n();
  const s = useSessionController();
  const languages = <LanguageSwitcher value={locale} onChange={onLocaleChange} inline />;
  if (s.loading) return null;
  if (s.loadFailed) return <main className="grid min-h-screen place-items-center p-4"><p role="alert" className="font-semibold">{t('platform.state.error')}</p></main>;
  if (s.stage !== 'in' || !s.me)
    return <SignInPage stage={s.stage === 'in' ? 'signedOut' : s.stage} me={s.me} hasLink={s.hasLink} signOnFailed={s.signOnFailed} keyFailed={s.keyFailed}
      busy={s.busy} languageSwitcher={languages} onSignOn={s.onSignOn} onTouchKey={s.onTouchKey} onRegisterKey={s.onRegisterKey} onSignOut={s.onSignOut} />;
  const items = navFor(s.me.role);
  return (
    <ConsoleShell displayName={s.me.displayName} role={s.me.role} items={items} languageSwitcher={languages} signingOut={s.signingOut} onSignOut={s.onSignOut}>
      <Routes>
        <Route path="/team" element={<TeamRoute />} />
        <Route path="/organisations" element={<OrganisationQueueRoute />} />
        <Route path="/organisations/:tenantId" element={<OrganisationDetailRoute role={s.me.role} />} />
        <Route path="/audit" element={<AuditRoute canExport={s.me.role === 'Owner'} />} />
        <Route path="*" element={<OverviewPage displayName={s.me.displayName} role={s.me.role} items={items} />} />
      </Routes>
    </ConsoleShell>
  );
}
