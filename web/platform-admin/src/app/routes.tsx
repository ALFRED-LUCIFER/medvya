import AuditPage from '../features/audit/components/AuditPage';
import { useAuditController } from '../features/audit/hooks/useAuditController';
import OrganisationDetailPage from '../features/organisations/components/OrganisationDetailPage';
import OrganisationQueuePage from '../features/organisations/components/OrganisationQueuePage';
import { useOrganisationDetailController } from '../features/organisations/hooks/useOrganisationDetailController';
import { useOrganisationQueueController } from '../features/organisations/hooks/useOrganisationQueueController';
import type { PlatformRole } from '../features/session/domain/session';
import TeamPage from '../features/team/components/TeamPage';
import { useTeamController } from '../features/team/hooks/useTeamController';

export function TeamRoute() {
  return <TeamPage vm={useTeamController()} />;
}

export function AuditRoute({ canExport }: { canExport: boolean }) {
  return <AuditPage vm={useAuditController(canExport)} />;
}

export function OrganisationQueueRoute() {
  return <OrganisationQueuePage vm={useOrganisationQueueController()} />;
}

export function OrganisationDetailRoute({ role }: { role: PlatformRole }) {
  return <OrganisationDetailPage vm={useOrganisationDetailController(role)} />;
}
