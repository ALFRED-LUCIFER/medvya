import { getJson, post } from '../../api/services/api';
import type { InviteForm, IssuedLink, Team } from '../domain/team';

export const fetchTeam = () => getJson<Team>('/v1/team');

export async function invite(form: InviteForm): Promise<IssuedLink> {
  const res = await post('/v1/team/invitations', { ...form, accessEndsOn: form.accessEndsOn || null });
  return res.json() as Promise<IssuedLink>;
}

export const recoverKey = async (memberId: string, reason: string) =>
  (await post(`/v1/team/${encodeURIComponent(memberId)}/recovery`, { reason })).json() as Promise<IssuedLink>;

export const removeMember = async (memberId: string, reason: string) => { await post(`/v1/team/${encodeURIComponent(memberId)}/removal`, { reason }); };

export const recordAccessReview = async (reason: string) => { await post('/v1/team/access-review', { reason }); };

export const pageOrigin = () => window.location.origin;

export const copyText = (text: string) => navigator.clipboard.writeText(text);
