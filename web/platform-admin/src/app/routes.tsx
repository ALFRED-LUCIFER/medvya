import ApprovalsPage from '../features/approvals/components/ApprovalsPage';
import { useApprovalsController } from '../features/approvals/hooks/useApprovalsController';
import type { PlatformRole } from '../features/session/domain/session';
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

export function ApprovalsRoute({ role, operator }: { role: PlatformRole; operator: string }) {
  return <ApprovalsPage vm={useApprovalsController(role)} operator={operator} />;
}
