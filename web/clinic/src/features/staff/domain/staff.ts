import { z } from 'zod';
import type { Access, Capability, Role } from '../../session/domain/session';

// Mirrors src/Host.Api/Staff/StaffEndpoints.cs and IStaffRoster.cs (the server is the authority; these checks only save a round trip).

/** Roles an admin can add here. Doctors are listed in the picker but are added with licence verification (Settings → Doctors). */
export const assignableRoles = ['Receptionist', 'Nurse', 'CareCoordinator', 'PracticeAdmin'] as const satisfies readonly Role[];
export type AssignableRole = (typeof assignableRoles)[number];

export type MembershipStatus = 'Invited' | 'Active' | 'Suspended' | 'Offboarded';
export type InvitationState = 'pending' | 'expired' | 'revoked';

export type StaffMember = {
  membershipId: string;
  name: string | null;
  email: string | null;
  role: Role;
  customRoleId: string | null;
  customRoleName: string | null;
  facilityId: string;
  facilityName: string;
  status: MembershipStatus;
  mfa: 'enrolled' | 'pending';
  invitationId: string | null;
  invitationState: InvitationState | null;
  invitationExpiresAt: string | null;
};

/** One matrix row of a built-in role. `extra` is the access a custom role could add here (practice admin's level), or null. */
export type RoleRow = { capability: Capability; access: Access; extra: Access | null };
export type BuiltInRole = { role: AssignableRole; rows: RoleRow[] };
export type CustomRole = { id: string; name: string; baseRole: AssignableRole; capabilities: Capability[] };
export type RoleCatalogue = { builtIn: BuiltInRole[]; custom: CustomRole[] };

export const addStaffSchema = z.object({
  email: z.email().max(254),
  facilityId: z.uuid(),
});
export type AddStaffForm = z.infer<typeof addStaffSchema>;

export const roleSchema = z.object({ name: z.string().trim().min(1).max(60), baseRole: z.enum(assignableRoles) });
export type RoleForm = z.infer<typeof roleSchema>;

/** What the admin picked in step 1: a built-in role, a custom role, or Doctor (which creates nothing here). */
export type RoleChoice = { kind: 'builtIn'; role: AssignableRole } | { kind: 'custom'; id: string } | { kind: 'doctor' };

/** The rows a person with this choice will hold, all locked here: the built-in matrix column, plus a custom role's added rows. */
export function permissionRows(catalogue: RoleCatalogue, choice: RoleChoice): { capability: Capability; access: Access; added: boolean }[] {
  if (choice.kind === 'doctor') return [];
  const custom = choice.kind === 'custom' ? catalogue.custom.find(c => c.id === choice.id) : undefined;
  const base = catalogue.builtIn.find(b => b.role === (custom ? custom.baseRole : choice.kind === 'builtIn' ? choice.role : null));
  return (base?.rows ?? []).map(r => {
    const added = !!custom?.capabilities.includes(r.capability) && r.extra !== null;
    return { capability: r.capability, access: added ? r.extra! : r.access, added };
  });
}

/** Rows a custom role on this base role may add: those the base role has no access to and practice admin has. */
export const addableRows = (catalogue: RoleCatalogue, baseRole: AssignableRole) =>
  (catalogue.builtIn.find(b => b.role === baseRole)?.rows ?? []).filter(r => r.extra !== null);

export const canResend = (m: StaffMember) => m.status === 'Invited' && m.invitationId !== null;
export const canRevoke = (m: StaffMember) => m.status === 'Invited' && m.invitationState === 'pending';

/** Offboarding applies to someone who has joined; a pending invitation is revoked instead. */
export const canOffboard = (m: StaffMember) => m.status === 'Active' || m.status === 'Suspended';
export const offboardSchema = z.object({ reason: z.string().trim().min(1).max(500) });
export type OffboardForm = z.infer<typeof offboardSchema>;

export type Step = 'role' | 'scope' | 'permissions';
