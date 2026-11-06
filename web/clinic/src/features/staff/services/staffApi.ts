import { csrf, post, send } from '../../schedule/services/scheduleApi';
import type { Capability } from '../../session/domain/session';
import type { AccessReview } from '../domain/accessReview';
import type { AssignableRole, CustomRole, RoleCatalogue, StaffMember } from '../domain/staff';

// Settings → Staff (F-10, MDV-P1-008). Ids in paths, details in bodies. The invitation link itself is only ever emailed.
export const fetchStaff = () => send<StaffMember[]>('/v1/staff');
export const fetchRoles = () => send<RoleCatalogue>('/v1/staff/roles');

export const addStaff = (body: { email: string; role: AssignableRole; customRoleId: string | null; facilityId: string }) =>
  post<{ id: string; expiresAt: string }>('/v1/staff', body);
export const resendInvitation = (id: string) => post<{ id: string; expiresAt: string }>(`/v1/staff/invitations/${id}/resend`, {});
export const revokeInvitation = (id: string) => send<void>(`/v1/invitations/${id}`, { method: 'DELETE', headers: csrf });

type RoleBody = { name: string; baseRole: AssignableRole; capabilities: Capability[] };
const json = (method: string, body: unknown) => ({ method, headers: { ...csrf, 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
export const createRole = (body: RoleBody) => post<CustomRole>('/v1/staff/roles', body);
export const updateRole = (id: string, body: RoleBody) => send<CustomRole>(`/v1/staff/roles/${id}`, json('PUT', body));
export const offboardStaff = (membershipId: string, reason: string) => post<void>(`/v1/staff/${membershipId}/offboard`, { reason });

// Quarterly access review (MDV-P1-010). Offboarding a person in the review uses offboardStaff above, which the server also records as their decision.
export const fetchAccessReview = () => send<AccessReview>('/v1/staff/access-review');
export const confirmAccess = (membershipId: string) => post<void>(`/v1/staff/access-review/${membershipId}/confirm`, {});
