import AuditPage from '../features/audit/components/AuditPage';
import { useAuditController } from '../features/audit/hooks/useAuditController';
import TeamPage from '../features/team/components/TeamPage';
import { useTeamController } from '../features/team/hooks/useTeamController';

export function TeamRoute() {
  return <TeamPage vm={useTeamController()} />;
}

export function AuditRoute({ canExport }: { canExport: boolean }) {
  return <AuditPage vm={useAuditController(canExport)} />;
}
