import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { inviteSchema, type InviteInput } from '../domain/invitation';
import { createInvitation, fetchFacilities, HttpError } from '../services/invitationsApi';

/** Minimal admin "Invite colleague" form (MDV-P0-013); the full staff screen is F-10 (MDV-P1-008). */
export function useInviteColleagueController() {
  const facilities = useQuery({ queryKey: ['invitation-facilities'], queryFn: fetchFacilities, retry: false });
  const form = useForm<InviteInput>({ resolver: zodResolver(inviteSchema), defaultValues: { email: '', role: 'Receptionist', facilityId: '' } });
  const invite = useMutation({ mutationFn: createInvitation, onSuccess: () => form.reset({ ...form.getValues(), email: '' }) });
  const error = facilities.error;
  return {
    form,
    loading: facilities.isPending,
    forbidden: error instanceof HttpError && (error.status === 401 || error.status === 403),
    loadFailed: facilities.isError,
    facilities: facilities.data ?? [],
    sending: invite.isPending,
    sent: invite.data ?? null,
    sendFailed: invite.isError,
    onSubmit: form.handleSubmit(values => invite.mutate(values)),
  };
}
