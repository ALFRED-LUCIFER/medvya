import type { ReactNode } from 'react';
import { Link } from 'react-router';
import { useI18n } from '@medvya/i18n';
import { Button, buttonVariants } from '@medvya/ui';
import type { Role } from '../../session/domain/session';
import type { NavGroup, NavItem } from '../domain/navigation';
import { navIcons } from './navIcons';

type Props = {
  displayName: string; role: Role; facilityName: string;
  groups: { group: NavGroup; items: NavItem[] }[];
  languageSwitcher: ReactNode; signingOut: boolean; canSwitchFacility: boolean; onSignOut: () => void;
};

/** Mobile "More" tab: every screen this role may open, plus account and language (the sidebar's job on wider screens). */
export default function MorePage({ displayName, role, facilityName, groups, languageSwitcher, signingOut, canSwitchFacility, onSignOut }: Props) {
  const { t } = useI18n();
  return (
    <div className="flex max-w-[560px] flex-col gap-5">
      <h1 className="text-xl font-extrabold">{t('nav.more')}</h1>
      <div className="rounded-xl border bg-card p-4">
        <p className="font-bold">{displayName}</p>
        <p className="text-sm text-muted-foreground">{t('shell.roleAtFacility', { role: t(`role.${role}`), facility: facilityName })}</p>
      </div>
      {canSwitchFacility && <Link to="/choose-facility" className={buttonVariants({ variant: 'outline', className: 'min-h-12' })}>{t('session.switchFacility')}</Link>}
      {groups.map(g => (
        <section key={g.group} className="flex flex-col gap-1">
          <h2 className="text-xs font-bold tracking-[.09em] text-muted-foreground uppercase">{t(`nav.group.${g.group}`)}</h2>
          <ul className="flex flex-col divide-y rounded-xl border bg-card">
            {g.items.map(item => {
              const Icon = navIcons[item.id];
              return (
                <li key={item.id}>
                  <Link to={item.path} className="flex min-h-12 items-center gap-3 px-4 font-semibold">
                    <Icon className="size-5 text-muted-foreground" aria-hidden />{t(`nav.${item.id}`)}
                  </Link>
                </li>
              );
            })}
          </ul>
        </section>
      ))}
      {languageSwitcher}
      <Button variant="outline" className="min-h-12" loading={signingOut} onClick={onSignOut}>{t('session.signOut')}</Button>
    </div>
  );
}
