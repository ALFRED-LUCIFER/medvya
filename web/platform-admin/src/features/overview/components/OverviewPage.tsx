import { Link } from 'react-router';
import { useI18n } from '@medvya/i18n';
import { Card, PageContainer, PageHeader } from '@medvya/ui';
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
    <PageContainer width="reading">
      <PageHeader title={t('platform.overview.title')} description={t('platform.overview.welcome', { name: displayName, role: t(`platform.role.${role}`) })} />
      <ul className="grid gap-3 sm:grid-cols-2">
        {items.flatMap(i => (i.id === 'overview' ? [] : [{ ...i, blurb: blurbs[i.id] }])).map(i => (
          <li key={i.id}>
            {/* The link covers the whole card, so the card stays one 44 px+ target. */}
            <Card className="relative h-full hover:bg-accent" intro={t(i.blurb)}
              title={<Link to={i.path} className="after:absolute after:inset-0 after:rounded-lg">{t(`platform.nav.${i.id}`)}</Link>} />
          </li>
        ))}
      </ul>
      <p className="text-sm text-muted-foreground">{t('platform.policy.long')}</p>
    </PageContainer>
  );
}
