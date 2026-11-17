import { useI18n, type Locale } from '@medvya/i18n';
import { Route, Routes } from 'react-router';
import { LanguageSwitcher } from '@medvya/ui';
import SignInPage from '../features/session/components/SignInPage';
import { useSessionController } from '../features/session/hooks/useSessionController';
import ConsoleShell from '../features/shell/components/ConsoleShell';
import { navFor } from '../features/shell/domain/navigation';
import OverviewPage from '../features/overview/components/OverviewPage';
import { ApprovalsRoute, AuditRoute, FeaturesRoute, MedicinesRoute, PlansRoute, TeamRoute } from './routes';

export default function App({ locale, onLocaleChange }: { locale: Locale; onLocaleChange: (l: Locale) => void }) {
  const { t } = useI18n();
  const s = useSessionController();
  const languages = <LanguageSwitcher value={locale} onChange={onLocaleChange} inline />;
  if (s.loading) return null;
  if (s.loadFailed) return <main className="grid min-h-screen place-items-center p-4"><p role="alert" className="font-semibold">{t('platform.state.error')}</p></main>;
  if (s.stage !== 'in' || !s.me)
    return <SignInPage stage={s.stage === 'in' ? 'signedOut' : s.stage} me={s.me} hasLink={s.hasLink} signOnFailed={s.signOnFailed} keyProblem={s.keyProblem}
      busy={s.busy} languageSwitcher={languages} onSignOn={s.onSignOn} onTouchKey={s.onTouchKey} onRegisterKey={s.onRegisterKey} onSignOut={s.onSignOut} />;
  const items = navFor(s.me.role);
  return (
    <ConsoleShell displayName={s.me.displayName} email={s.me.email} role={s.me.role} items={items} languageSwitcher={languages} signingOut={s.signingOut} onSignOut={s.onSignOut}>
      <Routes>
        <Route path="/approvals/:tenantId?" element={<ApprovalsRoute role={s.me.role} operator={s.me.displayName} />} />
        <Route path="/features" element={<FeaturesRoute role={s.me.role} />} />
        <Route path="/plans" element={<PlansRoute canEdit={s.me.role === 'Owner' || s.me.role === 'Finance'} />} />
        <Route path="/medicines" element={<MedicinesRoute role={s.me.role} />} />
        <Route path="/team" element={<TeamRoute />} />
        <Route path="/audit" element={<AuditRoute canExport={s.me.role === 'Owner'} />} />
        <Route path="*" element={<OverviewPage displayName={s.me.displayName} role={s.me.role} items={items} />} />
      </Routes>
    </ConsoleShell>
  );
}
