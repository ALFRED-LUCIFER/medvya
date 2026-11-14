import type { ReactNode } from 'react';
import { Link } from 'react-router';
import { useI18n } from '@medvya/i18n';
import { Button, buttonVariants, Card, PageContainer, PageHeader } from '@medvya/ui';
import type { Role } from '../../session/domain/session';
import type { NavItem } from '../domain/navigation';
import { navIcons } from './navIcons';

type Props = {
  displayName: string; role: Role; facilityName: string;
  items: NavItem[];
  languageSwitcher: ReactNode; signingOut: boolean; canSwitchFacility: boolean; onSignOut: () => void; devicesPath: string;
};

/** Mobile "More" tab: every screen this role may open, plus account and language (the sidebar's job on wider screens). */
export default function MorePage({ displayName, role, facilityName, items, languageSwitcher, signingOut, canSwitchFacility, onSignOut, devicesPath }: Props) {
  const { t } = useI18n();
  return (
    <PageContainer width="form" className="gap-5">
      <PageHeader title={t('nav.more')} />
      <Card className="gap-1">
        <p className="font-bold">{displayName}</p>
        <p className="text-sm text-muted-foreground">{t('shell.roleAtFacility', { role: t(`role.${role}`), facility: facilityName })}</p>
      </Card>
      {canSwitchFacility && <Link to="/choose-facility" className={buttonVariants({ variant: 'outline', className: 'min-h-12' })}>{t('session.switchFacility')}</Link>}
      <Link to={devicesPath} className={buttonVariants({ variant: 'outline', className: 'min-h-12' })}>{t('myDevices.link')}</Link>
      <Card className="gap-0 p-0">
        <ul className="flex flex-col divide-y">
          {items.map(item => {
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
      </Card>
      {languageSwitcher}
      <Button variant="outline" className="min-h-12" loading={signingOut} onClick={onSignOut}>{t('session.signOut')}</Button>
    </PageContainer>
  );
}
