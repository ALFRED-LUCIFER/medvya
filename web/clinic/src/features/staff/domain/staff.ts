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
  userId: string | null;
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
  /** Set the practice up (MDV-P1-107): only an owner offboards an owner or a doctor. */
  isOwner: boolean;
};

/** One matrix row of a built-in role. `extra` is the access a custom role could add here (practice admin's level), or null. */
export type RoleRow = { capability: Capability; access: Access; extra: Access | null };
export type BuiltInRole = { role: AssignableRole; rows: RoleRow[] };
export type CustomRole = { id: string; name: string; baseRole: AssignableRole; capabilities: Capability[] };
export type RoleCatalogue = { builtIn: BuiltInRole[]; custom: CustomRole[] };

export const addStaffSchema = z.object({
  email: z.email().max(254),
  facilityId: z.uuid(),
  /** Other locations they also work at (MDV-P1-071); the invitation carries them. */
  alsoFacilityIds: z.array(z.uuid()),
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

/**
 * The status column's word: an invitation that was revoked or ran out reads as such, not as "Invited" (MDV-P1-052); the
 * pending one keeps "Invited" with its expiry date underneath.
 */
export const statusLabel = (m: StaffMember): MembershipStatus | 'Revoked' | 'Expired' =>
  m.status === 'Invited' && m.invitationState === 'revoked' ? 'Revoked' : m.status === 'Invited' && m.invitationState === 'expired' ? 'Expired' : m.status;

/** A revoked invitation is not resent (#912; the server refuses it too): the row stays as the record, and the person is added again. */
export const canResend = (m: StaffMember) => m.status === 'Invited' && m.invitationId !== null && m.invitationState !== 'revoked';
export const canRevoke = (m: StaffMember) => m.status === 'Invited' && m.invitationState === 'pending';

/** Offboarding applies to someone who has joined; a pending invitation is revoked instead. */
export const canOffboard = (m: StaffMember) => m.status === 'Active' || m.status === 'Suspended';
/**
 * Why this signed-in user is not offered Offboard on a row they could otherwise offboard (MDV-P1-107), in words on the row; null when offered.
 * The server refuses the same: only an owner offboards an owner or a doctor (validation-plan question 18, the stricter answer).
 */
export function offboardBlock(m: StaffMember, me: string, iAmOwner: boolean): 'self' | 'owner' | 'doctor' | null {
  if (m.userId !== null && m.userId === me) return 'self';
  if (iAmOwner) return null;
  return m.isOwner ? 'owner' : isDoctor(m) ? 'doctor' : null;
}
/** The signed-in user owns the practice: one of their rows is an owner's. */
export const isOwnerAmong = (rows: StaffMember[], me: string) => rows.some(r => r.userId === me && r.isOwner && r.status === 'Active');

/** Change role in place (MDV-P1-107): an active member with a role the staff table assigns, never an owner or oneself. */
export const canChangeRole = (m: StaffMember, me: string) =>
  m.status === 'Active' && !m.isOwner && m.userId !== me && (assignableRoles as readonly string[]).includes(m.role);
/** The role picked, as one value: "builtIn:Nurse" or "custom:<id>". */
export const roleValue = (m: Pick<StaffMember, 'role' | 'customRoleId'>) => (m.customRoleId ? `custom:${m.customRoleId}` : `builtIn:${m.role}`);
export const roleOfValue = (catalogue: RoleCatalogue, value: string): { role: AssignableRole; customRoleId: string | null } | null => {
  const [kind, id] = value.split(':') as [string, string];
  if (kind === 'builtIn' && (assignableRoles as readonly string[]).includes(id)) return { role: id as AssignableRole, customRoleId: null };
  const custom = kind === 'custom' ? catalogue.custom.find(c => c.id === id) : undefined;
  return custom ? { role: custom.baseRole, customRoleId: custom.id } : null;
};

/** A doctor's future visits move to "needs rebooking": the admin says whether patients are offered new slots or called (#225). */
export const isDoctor = (m: StaffMember) => m.role === 'SoloDoctor' || m.role === 'HospitalDoctor';
export const offboardSchema = z.object({ reason: z.string().trim().min(1).max(500), rebooking: z.enum(['offer', 'call']).nullable() });
export type OffboardForm = z.infer<typeof offboardSchema>;
/** For a doctor the rebooking choice is required; for anyone else it does not apply. */
export const rebookingMissing = (m: StaffMember, v: OffboardForm) => isDoctor(m) && v.rebooking === null;

/** An offboarded person who had joined can be brought back with the same role (#225). */
export const canReactivate = (m: StaffMember) => m.status === 'Offboarded' && m.userId !== null;
export const reactivateSchema = z.object({ reason: z.string().trim().min(1).max(500) });
export type ReactivateForm = z.infer<typeof reactivateSchema>;

/** The locations a person works at: the active rows of the same user (one membership per location, MDV-P1-071). */
export const locationsOf = (rows: StaffMember[], m: StaffMember) =>
  m.userId === null ? [m.facilityId] : rows.filter(r => r.userId === m.userId && r.status === 'Active').map(r => r.facilityId);
export const canSetLocations = (m: StaffMember) => m.status === 'Active' && m.userId !== null;
export const locationsSchema = z.object({ facilityIds: z.array(z.uuid()).min(1), reason: z.string().trim().max(500) });
export type LocationsForm = z.infer<typeof locationsSchema>;
/** Taking a location away ends access there and needs a reason; only adding does not. */
export const removesLocation = (current: string[], chosen: string[]) => current.some(id => !chosen.includes(id));
export const reasonMissing = (current: string[], v: LocationsForm) => removesLocation(current, v.facilityIds) && v.reason === '';

export type Step = 'role' | 'scope' | 'permissions';
