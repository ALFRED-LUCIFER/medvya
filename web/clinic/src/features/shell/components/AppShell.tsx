import type { ReactNode } from 'react';
import { Link, NavLink } from 'react-router';
import { Bell, Menu, Search } from 'lucide-react';
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
  /** Patient search target (D-02 header entry), when this membership may search patients. */
  searchPath?: string | null;
  /** The ⌘K patient search (D-02, MDV-P2-003), composed by the app for roles that treat patients; it replaces the plain link to the directory. */
  globalSearch?: ReactNode;
  /** The notification centre (D-03), composed by the app for roles that may see notifications. */
  notifications?: ReactNode;
  /** The offline strip (MDV-P2-056): under the top bar, in the flow, so it covers nothing. */
  banner?: ReactNode;
  children: ReactNode;
};

/**
 * Responsive shell (DESIGN.md Navigation): bottom tabs and account in More below 768 px; a 72 px rail with a label under every icon
 * from 768 px (never icon-only, ruling 8); the 232 px sidebar from 1280 px. Settings sits at the foot of the menu, as in the design (Medvya Web). A 48 px top bar holds clinic,
 * search, bell and the account menu (56 px on phones, account in More).
 */
export default function AppShell({ displayName, role, facilityName, items, tabs, onLocaleChange, onSignOut, onMyDevices, onChangeFacility,
  searchPath, globalSearch, notifications, banner, children }: Props) {
  const { t } = useI18n();
  // Settings is pinned to the foot of the menu (design: Medvya Web sidebar); it stays last in reading order.
  const main = items.filter(i => i.id !== 'settings');
  const foot = items.filter(i => i.id === 'settings');
  // One list, drawn as the labelled rail under 1280 px and as the sidebar from 1280 px.
  const navLists = (list: NavItem[], className: string) => (
    <div className={className}>
      <ul className="flex flex-col gap-1 lg:hidden">
        {list.map(item => (
          <li key={item.id}>
            <NavRailItem asChild icon={navIcons[item.id]} label={t(`nav.${item.id}`)}><NavLink to={item.path} end /></NavRailItem>
          </li>
        ))}
      </ul>
      <ul className="hidden flex-col gap-1 lg:flex">
        {list.map(item => {
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
    </div>
  );
  return (
    // --shell-top is the top bar's height: a patient strip pins right under it (MDV-P1-092).
    <div className="min-h-screen [--shell-top:var(--spacing-topbar-phone)] md:grid md:grid-cols-[auto_minmax(0,1fr)] md:[--shell-top:var(--spacing-topbar)]">
      <a href="#main" className="sr-only rounded-md bg-card px-4 py-3 font-bold text-foreground focus:not-sr-only focus:fixed focus:start-4 focus:top-4 focus:z-50">
        {t('shell.skip')}
      </a>

      {/* Navy surface: the focus ring switches to white for contrast. */}
      <nav aria-label={t('shell.mainNav')}
        className="sticky top-0 hidden h-screen w-rail flex-col gap-1 overflow-y-auto bg-brand-950 px-1 py-3 text-nav-ink md:flex lg:w-sidebar lg:px-3 print:hidden [&_:focus-visible]:outline-white">
        <div className="flex items-center justify-center gap-2.5 pb-3 lg:justify-start lg:px-2">
          <span aria-hidden className="grid size-9 shrink-0 place-items-center rounded-[10px] bg-brand-700 font-extrabold text-white">{t('app.name').charAt(0)}</span>
          <p className="hidden text-heading text-white lg:block">{t('app.name')}</p>
        </div>

        {navLists(main, '')}
        {foot.length > 0 && navLists(foot, 'mt-auto border-t border-nav-line pt-3')}
      </nav>

      <div className="flex min-w-0 flex-col">
        <header data-pinned="top" className="sticky top-0 z-10 flex h-(--shell-top) items-center gap-3 border-b bg-card px-4 md:px-6 print:hidden">
          <p className="min-w-0 flex-1 truncate font-bold">{facilityName}</p>
          {globalSearch ?? (searchPath && (
            <Link to={searchPath} className={cn(buttonVariants({ variant: 'outline' }), 'max-sm:size-control max-sm:px-0')}>
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
        {banner}
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
