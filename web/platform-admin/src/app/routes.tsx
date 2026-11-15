import ApprovalsPage from '../features/approvals/components/ApprovalsPage';
import { useApprovalsController } from '../features/approvals/hooks/useApprovalsController';
import type { PlatformRole } from '../features/session/domain/session';
import AuditPage from '../features/audit/components/AuditPage';
import { useAuditController } from '../features/audit/hooks/useAuditController';
import FeaturesPage from '../features/features/components/FeaturesPage';
import { useFeaturesController } from '../features/features/hooks/useFeaturesController';
import MedicinesPage from '../features/medicines/components/MedicinesPage';
import { useMedicinesController } from '../features/medicines/hooks/useMedicinesController';
import PlansPage from '../features/plans/components/PlansPage';
import { usePlansController } from '../features/plans/hooks/usePlansController';
import TeamPage from '../features/team/components/TeamPage';
import { useTeamController } from '../features/team/hooks/useTeamController';

export function TeamRoute() {
  return <TeamPage vm={useTeamController()} />;
}

export function PlansRoute({ canEdit }: { canEdit: boolean }) {
  return <PlansPage vm={usePlansController(canEdit)} />;
}

export function AuditRoute({ canExport }: { canExport: boolean }) {
  return <AuditPage vm={useAuditController(canExport)} />;
}

export function FeaturesRoute({ role }: { role: PlatformRole }) {
  return <FeaturesPage vm={useFeaturesController(role)} />;
}

export function MedicinesRoute({ role }: { role: PlatformRole }) {
  return <MedicinesPage vm={useMedicinesController(role)} />;
}

export function ApprovalsRoute({ role, operator }: { role: PlatformRole; operator: string }) {
  return <ApprovalsPage vm={useApprovalsController(role)} operator={operator} />;
}
