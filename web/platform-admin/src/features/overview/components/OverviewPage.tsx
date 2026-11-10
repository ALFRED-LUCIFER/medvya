import { Link } from 'react-router';
import { useI18n } from '@medvya/i18n';
import type { PlatformRole } from '../../session/domain/session';
import type { NavItem } from '../../shell/domain/navigation';

/**
 * Platform overview. shortcut: no tenant counts, rollouts or attention list yet; they need the tenant catalogue (MDV-P5-017) and
 * rollouts (MDV-P5-041), which read the control plane through their own reviewed paths.
 */
const blurbs = { approvals: 'platform.overview.approvals', team: 'platform.overview.team', audit: 'platform.overview.audit' } as const;

export default function OverviewPage({ displayName, role, items }: { displayName: string; role: PlatformRole; items: NavItem[] }) {
  const { t } = useI18n();
  return (
    <>
      <h1 className="text-2xl font-extrabold">{t('platform.overview.title')}</h1>
      <p>{t('platform.overview.welcome', { name: displayName, role: t(`platform.role.${role}`) })}</p>
      <ul className="grid gap-3 sm:grid-cols-2">
        {items.flatMap(i => (i.id === 'overview' ? [] : [{ ...i, blurb: blurbs[i.id] }])).map(i => (
          <li key={i.id}>
            <Link to={i.path} className="flex min-h-11 flex-col gap-1 rounded-lg border border-border bg-card p-4 hover:bg-accent">
              <span className="font-bold">{t(`platform.nav.${i.id}`)}</span>
              <span className="text-sm text-muted-foreground">{t(i.blurb)}</span>
            </Link>
          </li>
        ))}
      </ul>
      <p className="text-sm text-muted-foreground">{t('platform.policy.long')}</p>
    </>
  );
}
