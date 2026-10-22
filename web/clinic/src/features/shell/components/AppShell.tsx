import type { ReactNode } from 'react';
import { Link, NavLink } from 'react-router';
import { Bell, Menu, Search } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, buttonVariants, cn } from '@medvya/ui';
import type { Role } from '../../session/domain/session';
import type { NavGroup, NavItem } from '../domain/navigation';
import { navIcons, railColour } from './navIcons';

type Props = {
  displayName: string;
  role: Role;
  facilityName: string;
  groups: { group: NavGroup; items: NavItem[] }[];
  tabs: NavItem[];
  languageSwitcher: ReactNode;
  signingOut: boolean;
  onSignOut: () => void;
  /** Patient search target (D-02 header entry), when this membership may search patients. */
  searchPath?: string | null;
  children: ReactNode;
};

const initials = (name: string) => name.replace(/^Dr\.?\s+/i, '').split(/\s+/).map(p => p[0]).slice(0, 2).join('').toUpperCase();

/**
 * Responsive shell (docs/02-design/responsive-and-dark.md): bottom tabs below 768 px, 72 px icon rail from 768 px,
 * full navy sidebar from 1280 px (the md/lg breakpoints in packages/ui/src/styles.css).
 */
export default function AppShell({ displayName, role, facilityName, groups, tabs, languageSwitcher, signingOut, onSignOut, searchPath, children }: Props) {
  const { t } = useI18n();
  return (
    <div className="min-h-screen md:grid md:grid-cols-[72px_minmax(0,1fr)] lg:grid-cols-[clamp(196px,22vw,232px)_minmax(0,1fr)]">
      <a href="#main" className="sr-only rounded-md bg-card px-4 py-3 font-bold text-foreground focus:not-sr-only focus:fixed focus:start-4 focus:top-4 focus:z-50">
        {t('shell.skip')}
      </a>

      {/* Sidebar (≥1280) and icon rail (768–1279). Navy surface: focus ring switches to white for contrast. */}
      <nav aria-label={t('shell.mainNav')}
        className="sticky top-0 hidden h-screen flex-col gap-1 overflow-y-auto bg-brand-950 px-3 py-4 text-nav-ink md:flex [&_:focus-visible]:outline-white">
        <div className="flex items-center gap-2.5 pb-4 max-lg:justify-center lg:px-2">
          <span aria-hidden className="grid size-9 shrink-0 place-items-center rounded-[10px] bg-brand-700 font-extrabold text-white">{t('app.name').charAt(0)}</span>
          <div className="hidden min-w-0 lg:block">
            <p className="text-[17px] font-extrabold text-white">{t('app.name')}</p>
            <p className="truncate text-xs text-nav-muted">{facilityName}</p>
          </div>
        </div>
        {groups.map(g => (
          <div key={g.group} className="flex flex-col gap-1 max-lg:pt-2">
            <h2 className="hidden px-2.5 pt-3 pb-1 text-[11px] font-bold tracking-[.09em] text-nav-muted uppercase lg:block">{t(`nav.group.${g.group}`)}</h2>
            <ul className="flex flex-col gap-1">
              {g.items.map(item => {
                const Icon = navIcons[item.id];
                return (
                  <li key={item.id}>
                    <NavLink to={item.path} end
                      className={({ isActive }) => cn(
                        'relative flex min-h-11 items-center gap-2.5 rounded-lg px-2.5 text-sm font-semibold hover:bg-nav-hover max-lg:mx-auto max-lg:size-12 max-lg:justify-center max-lg:px-0',
                        isActive && `bg-nav-hover text-white before:absolute before:inset-y-2 before:start-0 before:w-[3px] before:rounded-full ${railColour[item.group]}`,
                      )}>
                      <Icon className="size-5 shrink-0" aria-hidden />
                      <span className="truncate max-lg:sr-only">{t(`nav.${item.id}`)}</span>
                    </NavLink>
                  </li>
                );
              })}
            </ul>
          </div>
        ))}
        <div className="mt-auto flex items-center gap-2.5 border-t border-nav-line pt-3 max-lg:justify-center lg:px-2">
          <span aria-hidden className="grid size-8 shrink-0 place-items-center rounded-full bg-nav-line text-xs font-bold text-white">{initials(displayName)}</span>
          <div className="min-w-0 max-lg:sr-only">
            <p className="truncate text-[13px] font-semibold text-white">{displayName}</p>
            <p className="truncate text-xs text-nav-muted">{t(`role.${role}`)}</p>
          </div>
        </div>
      </nav>

      <div className="flex min-w-0 flex-col">
        <header className="sticky top-0 z-10 flex min-h-15 items-center gap-3 border-b bg-card px-4 md:px-6">
          <p className="min-w-0 flex-1 truncate font-bold lg:invisible">{facilityName}</p>
          {searchPath && (
            <Link to={searchPath} className={cn(buttonVariants({ variant: 'outline' }), 'max-sm:size-11 max-sm:px-0')}>
              <Search aria-hidden /><span className="max-sm:sr-only">{t('patients.search.open')}</span>
            </Link>
          )}
          <div className="hidden md:block">{languageSwitcher}</div>
          <Button variant="outline" className="hidden md:inline-flex" loading={signingOut} onClick={onSignOut}>{t('session.signOut')}</Button>
          {/* shortcut: bell is a placeholder; the notification centre (D-03) ships in P1 (MDV-P1 notification centre ticket). */}
          <Button variant="ghost" size="icon" disabled aria-label={t('shell.notifications')}><Bell className="size-5" aria-hidden /></Button>
        </header>
        <main id="main" tabIndex={-1} className="flex-1 p-4 pb-28 focus:outline-none md:p-6">{children}</main>
      </div>

      {/* Mobile bottom tabs (360–767 px): Today · Patients · Inbox · Messages · More, filtered to what this role may see. */}
      <nav aria-label={t('shell.mainNav')} className="fixed inset-x-0 bottom-0 z-10 grid h-[72px] auto-cols-fr grid-flow-col border-t bg-card md:hidden">
        {[...tabs.map(i => ({ key: i.id, to: i.path, Icon: navIcons[i.id], label: t(`nav.${i.id}`) })), { key: 'more', to: '/more', Icon: Menu, label: t('nav.more') }]
          .map(({ key, to, Icon, label }) => (
            <NavLink key={key} to={to} end
              className={({ isActive }) => cn('flex min-h-12 flex-col items-center justify-center gap-1 px-1 text-center text-[11px] leading-tight font-bold',
                isActive ? 'text-primary' : 'text-muted-foreground')}>
              <Icon className="size-6" aria-hidden />
              {label}
            </NavLink>
          ))}
      </nav>
    </div>
  );
}
