import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { inviteForm, isReviewDue, linkUrl, reasonForm, type InviteForm, type IssuedLink, type PendingAction, type ReasonForm, type ShownLink } from '../domain/team';
import { copyText, fetchTeam, invite, pageOrigin, recordAccessReview, recoverKey, removeMember } from '../services/teamApi';
import { ApiError } from '../../api/services/api';

const teamKey = ['platform', 'team'] as const;

export function useTeamController() {
  const client = useQueryClient();
  const team = useQuery({ queryKey: teamKey, queryFn: fetchTeam });
  const [inviting, setInviting] = useState(false);
  const [pending, setPending] = useState<PendingAction | null>(null);
  const [issued, setIssued] = useState<ShownLink | null>(null);
  const [copied, setCopied] = useState(false);
  const inviteFormState = useForm<InviteForm>({ resolver: zodResolver(inviteForm), defaultValues: { displayName: '', email: '', role: 'Support', accessEndsOn: '', reason: '' }, shouldFocusError: false });
  const reasonFormState = useForm<ReasonForm>({ resolver: zodResolver(reasonForm), defaultValues: { reason: '' }, shouldFocusError: false });
  const refresh = () => client.invalidateQueries({ queryKey: teamKey });
  const show = (link: IssuedLink, purpose: 'invite' | 'recover') => { setCopied(false); setIssued({ ...link, url: linkUrl(pageOrigin(), link.token), purpose }); };

  const sendInvite = useMutation({
    mutationFn: invite,
    onSuccess: link => { show(link, 'invite'); setInviting(false); inviteFormState.reset(); void refresh(); },
    onError: e => { if (e instanceof ApiError && e.fields.email?.includes('taken')) inviteFormState.setError('email', { message: 'taken' }); },
  });
  const act = useMutation({
    mutationFn: async ({ action, reason }: { action: PendingAction; reason: string }) => {
      if (action.kind === 'recover') show(await recoverKey(action.memberId, reason), 'recover');
      else if (action.kind === 'remove') await removeMember(action.memberId, reason);
      else await recordAccessReview(reason);
    },
    onSuccess: () => { setPending(null); reasonFormState.reset(); void refresh(); },
  });

  return {
    state: team.isPending ? 'loading' as const : team.isError ? 'error' as const : 'ready' as const,
    forbidden: team.error instanceof ApiError && team.error.status === 403,
    team: team.data ?? null,
    // Measured at the time the team was read, so rendering stays pure.
    reviewDue: team.data ? isReviewDue(team.data, new Date(team.dataUpdatedAt)) : false,
    inviting, inviteForm: inviteFormState, inviteBusy: sendInvite.isPending, inviteFailed: sendInvite.isError && !inviteFormState.formState.errors.email,
    onStartInvite: () => { setIssued(null); setInviting(true); },
    onCancelInvite: () => setInviting(false),
    onInvite: inviteFormState.handleSubmit(form => sendInvite.mutate(form)),
    pending, reasonForm: reasonFormState, actionBusy: act.isPending,
    // 409: yourself, or the last owner. The server refuses; the screen says why.
    actionRefused: act.error instanceof ApiError && act.error.status === 409,
    actionFailed: act.isError && !(act.error instanceof ApiError && act.error.status === 409),
    onStartAction: (action: PendingAction) => { act.reset(); reasonFormState.reset(); setIssued(null); setPending(action); },
    onCancelAction: () => setPending(null),
    onConfirmAction: reasonFormState.handleSubmit(({ reason }) => { if (pending) act.mutate({ action: pending, reason }); }),
    issued, copied,
    onCopyLink: () => { if (issued) void copyText(issued.url).then(() => setCopied(true)); },
    onDismissLink: () => setIssued(null),
  };
}

