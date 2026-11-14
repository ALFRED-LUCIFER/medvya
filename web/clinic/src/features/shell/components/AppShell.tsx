import type { ReactNode } from 'react';
import { Link, NavLink } from 'react-router';
import { Bell, Menu, PanelLeftClose, PanelLeftOpen, Search } from 'lucide-react';
import { useI18n, type Locale } from '@medvya/i18n';
import { AccountMenu, Button, buttonVariants, cn, NavRailItem } from '@medvya/ui';
import type { Role } from '../../session/domain/session';
import type { NavItem } from '../domain/navigation';
import { navIcons, railColour } from './navIcons';

type Props = {
  displayName: string;
  role: Role;
  facilityName: string;
  /** The role's menu, in its order (blueprint section 3). */
  items: NavItem[];
  tabs: NavItem[];
  onLocaleChange: (locale: Locale) => void;
  onSignOut: () => void;
  /** "My devices" in the account menu (MDV-P0-012). */
  onMyDevices: () => void;
  /** Only with more than one active membership: the facility picker (MDV-P0-012). */
  onChangeFacility?: () => void;
  /** From 1280 px the user has folded the sidebar to the labelled rail (their own choice, MDV-P1-093). */
  navCollapsed: boolean;
  onToggleNav: () => void;
  /** Patient search target (D-02 header entry), when this membership may search patients. */
  searchPath?: string | null;
  /** The ⌘K patient search (D-02, MDV-P2-003), composed by the app for roles that treat patients; it replaces the plain link to the directory. */
  globalSearch?: ReactNode;
  /** The notification centre (D-03), composed by the app for roles that may see notifications. */
  notifications?: ReactNode;
  children: ReactNode;
};

/**
 * Responsive shell (DESIGN.md Navigation): bottom tabs and account in More below 768 px; a 72 px rail with a label under every icon
 * from 768 px (never icon-only, ruling 8); the 232 px sidebar from 1280 px unless the user folds it. A 48 px top bar holds clinic,
 * search, bell and the account menu (56 px on phones, account in More).
 */
export default function AppShell({ displayName, role, facilityName, items, tabs, onLocaleChange, onSignOut, onMyDevices, onChangeFacility,
  navCollapsed, onToggleNav, searchPath, globalSearch, notifications, children }: Props) {
  const { t } = useI18n();
  // From 1280 px: the sidebar, or the rail when folded. Below 1280 the rail is the only choice.
  const sidebarOnly = navCollapsed ? 'hidden' : 'hidden lg:flex';
  const railOnly = navCollapsed ? '' : 'lg:hidden';
  return (
    // --shell-top is the top bar's height: a patient strip pins right under it (MDV-P1-092).
    <div className="min-h-screen [--shell-top:var(--spacing-topbar-phone)] md:grid md:grid-cols-[auto_minmax(0,1fr)] md:[--shell-top:var(--spacing-topbar)]">
      <a href="#main" className="sr-only rounded-md bg-card px-4 py-3 font-bold text-foreground focus:not-sr-only focus:fixed focus:start-4 focus:top-4 focus:z-50">
        {t('shell.skip')}
      </a>

      {/* Navy surface: the focus ring switches to white for contrast. */}
      <nav aria-label={t('shell.mainNav')}
        className={cn('sticky top-0 hidden h-screen w-rail flex-col gap-1 overflow-y-auto bg-brand-950 px-1 py-3 text-nav-ink md:flex print:hidden [&_:focus-visible]:outline-white',
          !navCollapsed && 'lg:w-sidebar lg:px-3')}>
        <div className={cn('flex items-center justify-center gap-2.5 pb-3', !navCollapsed && 'lg:justify-start lg:px-2')}>
          <span aria-hidden className="grid size-9 shrink-0 place-items-center rounded-[10px] bg-brand-700 font-extrabold text-white">{t('app.name').charAt(0)}</span>
          <p className={cn(sidebarOnly, 'text-heading text-white')}>{t('app.name')}</p>
        </div>

        <ul className={cn('flex flex-col gap-1', railOnly)}>
          {items.map(item => (
            <li key={item.id}>
              <NavRailItem asChild icon={navIcons[item.id]} label={t(`nav.${item.id}`)}><NavLink to={item.path} end /></NavRailItem>
            </li>
          ))}
        </ul>

        <ul className={cn(sidebarOnly, 'flex-col gap-1')}>
          {items.map(item => {
            const Icon = navIcons[item.id];
            return (
              <li key={item.id}>
                <NavLink to={item.path} end
                  className={({ isActive }) => cn(
                    'relative flex min-h-control items-center gap-2.5 rounded-lg px-2.5 text-label hover:bg-nav-hover',
                    isActive && `bg-nav-hover text-white before:absolute before:inset-y-2 before:start-0 before:w-[3px] before:rounded-full ${railColour[item.group]}`,
                  )}>
                  <Icon className="size-5 shrink-0" aria-hidden />
                  <span className="truncate">{t(`nav.${item.id}`)}</span>
                </NavLink>
              </li>
            );
          })}
        </ul>

        {/* The fold toggle exists only where there is a choice (1280 px and up). The icons follow the reading direction. */}
        <div className="mt-auto hidden border-t border-nav-line pt-3 lg:block">
          {navCollapsed
            ? <NavRailItem asChild icon={PanelLeftOpen} label={t('shell.expandNav')} className="rtl:[&_svg]:-scale-x-100"><button type="button" onClick={onToggleNav} /></NavRailItem>
            : <button type="button" onClick={onToggleNav} className="flex min-h-control w-full items-center gap-2.5 rounded-lg px-2.5 text-label hover:bg-nav-hover">
                <PanelLeftClose className="size-5 shrink-0 rtl:-scale-x-100" aria-hidden />{t('shell.collapseNav')}
              </button>}
        </div>
      </nav>

      <div className="flex min-w-0 flex-col">
        <header data-pinned="top" className="sticky top-0 z-10 flex h-(--shell-top) items-center gap-3 border-b bg-card px-4 md:px-6 print:hidden">
          <p className="min-w-0 flex-1 truncate font-bold">{facilityName}</p>
          {globalSearch ?? (searchPath && (
            <Link to={searchPath} className={cn(buttonVariants({ variant: 'outline' }), 'max-sm:size-11 max-sm:px-0')}>
              <Search aria-hidden /><span className="max-sm:sr-only">{t('patients.search.open')}</span>
            </Link>
          ))}
          {/* The notification centre (D-03) for roles that may see any notification; others keep the inert bell. */}
          {notifications ?? <Button variant="ghost" size="icon" disabled aria-label={t('shell.notifications')}><Bell className="size-5" aria-hidden /></Button>}
          {/* Under 768 px the account items are in More. */}
          <div className="hidden md:block">
            <AccountMenu name={displayName} roleLabel={t(`role.${role}`)} onLocaleChange={onLocaleChange} onMyDevices={onMyDevices}
              onChangeFacility={onChangeFacility} onSignOut={onSignOut} />
          </div>
        </header>
        <main id="main" tabIndex={-1} className="flex-1 p-4 pb-28 focus:outline-none md:p-6">{children}</main>
      </div>

      {/* Mobile bottom tabs (360–767 px): the first four items of the role's menu, then More. */}
      <nav aria-label={t('shell.mainNav')} className="fixed inset-x-0 bottom-0 z-10 grid h-[72px] auto-cols-fr grid-flow-col border-t bg-card md:hidden print:hidden">
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
