import type { ReactNode } from 'react';
import { NavLink } from 'react-router';
import { ClipboardCheck, ClipboardList, LayoutDashboard, LogOut, ShieldCheck, Users } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, cn } from '@medvya/ui';
import type { PlatformRole } from '../../session/domain/session';
import type { NavId, NavItem } from '../domain/navigation';

const icons: Record<NavId, typeof Users> = { overview: LayoutDashboard, approvals: ClipboardCheck, team: Users, audit: ClipboardList };

type Props = {
  displayName: string;
  role: PlatformRole;
  items: NavItem[];
  languageSwitcher: ReactNode;
  signingOut: boolean;
  onSignOut: () => void;
  children: ReactNode;
};

const initials = (name: string) => name.split(/\s+/).map(p => p[0]).slice(0, 2).join('').toUpperCase();

/** The console frame: navy sidebar from 768 px, a top bar below; a standing line that the console never shows patient records. */
export default function ConsoleShell({ displayName, role, items, languageSwitcher, signingOut, onSignOut, children }: Props) {
  const { t } = useI18n();
  return (
    <div className="min-h-screen md:grid md:grid-cols-[clamp(196px,22vw,232px)_minmax(0,1fr)]">
      <a href="#main" className="sr-only rounded-md bg-card px-4 py-3 font-bold text-foreground focus:not-sr-only focus:fixed focus:start-4 focus:top-4 focus:z-50">
        {t('shell.skip')}
      </a>
      <nav aria-label={t('platform.nav.label')} className="flex flex-col gap-1 bg-brand-950 px-3 py-4 text-nav-ink md:sticky md:top-0 md:h-screen [&_:focus-visible]:outline-white">
        <div className="flex items-center gap-2.5 px-2 pb-4">
          <span aria-hidden className="grid size-9 shrink-0 place-items-center rounded-[10px] bg-brand-700 font-extrabold text-white">{t('platform.brand.initial')}</span>
          <div className="min-w-0">
            <p className="text-[17px] font-extrabold text-white">{t('platform.brand.name')}</p>
            <p className="text-xs text-nav-muted">{t('platform.brand.staffOnly')}</p>
          </div>
        </div>
        <ul className="flex gap-1 max-md:overflow-x-auto md:flex-col">
          {items.map(item => {
            const Icon = icons[item.id];
            return (
              <li key={item.id}>
                <NavLink to={item.path} end className={({ isActive }) => cn(
                  'flex min-h-11 items-center gap-2.5 rounded-lg px-2.5 text-sm font-semibold whitespace-nowrap hover:bg-nav-hover',
                  isActive && 'bg-nav-hover text-white',
                )}>
                  <Icon className="size-5 shrink-0" aria-hidden />
                  {t(`platform.nav.${item.id}`)}
                </NavLink>
              </li>
            );
          })}
        </ul>
        <div className="mt-auto flex items-center gap-2.5 border-t border-nav-line px-2 pt-3 max-md:hidden">
          <span aria-hidden className="grid size-8 shrink-0 place-items-center rounded-full bg-nav-line text-xs font-bold text-white">{initials(displayName)}</span>
          <div className="min-w-0">
            <p className="truncate text-[13px] font-semibold text-white">{displayName}</p>
            <p className="truncate text-xs text-nav-muted">{t(`platform.role.${role}`)}</p>
          </div>
        </div>
      </nav>
      <div className="flex min-w-0 flex-col">
        {/* 48 px, like the clinic's top bar: the page title row then starts at the same height on every console page (MDV-P2-059). */}
        <header className="flex min-h-12 flex-wrap items-center justify-between gap-x-3 gap-y-1 border-b bg-card px-4 py-0.5">
          <p className="flex items-center gap-2 text-sm font-semibold text-muted-foreground">
            <ShieldCheck className="size-4 shrink-0" aria-hidden />
            {t('platform.policy.short')}
          </p>
          <div className="flex flex-wrap items-center gap-2">
            {languageSwitcher}
            <Button variant="outline" onClick={onSignOut} loading={signingOut}><LogOut aria-hidden />{t('platform.action.signOut')}</Button>
          </div>
        </header>
        <main id="main" tabIndex={-1} className="flex flex-col gap-4 p-4 md:p-6">{children}</main>
      </div>
    </div>
  );
}
