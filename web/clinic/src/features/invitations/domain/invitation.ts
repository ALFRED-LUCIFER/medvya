import { z } from 'zod';

import type { Access, Capability, Role } from '../../session/domain/session';

/** Staff roles an admin can invite (InvitationRules.CanInvite): every role but Patient, who joins through the portal. */
export const invitableRoles = ['Receptionist', 'Nurse', 'CareCoordinator', 'SoloDoctor', 'HospitalDoctor', 'PracticeAdmin', 'HospitalIt'] as const satisfies readonly Role[];

/** What /invite/:token shows (F-06): exactly the role's column of the role-permission matrix. */
export type InvitationPreview = {
  clinic: string;
  facility: string;
  inviter: string | null;
  role: Role;
  expiresAt: string;
  licenceVerificationRequired: boolean;
  capabilities: { capability: Capability; access: Access }[];
};

export const isAllowed = (access: Access) => access !== 'None';

export type Facility = { id: string; name: string };

export const inviteSchema = z.object({
  email: z.email().max(254),
  role: z.enum(invitableRoles),
  facilityId: z.uuid(),
});
export type InviteInput = z.infer<typeof inviteSchema>;
export type InvitationCreated = { id: string; expiresAt: string; licenceVerificationRequired: boolean };
