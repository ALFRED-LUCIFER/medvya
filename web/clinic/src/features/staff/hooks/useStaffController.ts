import { useState } from 'react';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { fetchLocations } from '../../settings/services/settingsApi';
import { ApiError } from '../../schedule/services/scheduleApi';
import { addStaffSchema, offboardSchema, type AddStaffForm, type OffboardForm, type StaffMember, type AssignableRole, type RoleChoice, type Step } from '../domain/staff';
import { addStaff, fetchRoles, fetchStaff, offboardStaff, resendInvitation, revokeInvitation } from '../services/staffApi';


/** Settings → Staff: the staff table and the "Add staff member" steps (role → location → permissions → invite). */
export function useStaffController(defaultFacilityId: string) {
  const queryClient = useQueryClient();
  const staff = useQuery({ queryKey: ['staff', 'list'], queryFn: fetchStaff });
  const roles = useQuery({ queryKey: ['staff', 'roles'], queryFn: fetchRoles });
  const locations = useQuery({ queryKey: ['settings', 'locations'], queryFn: fetchLocations });
  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['staff', 'list'] });

  const [step, setStep] = useState<Step | null>(null);
  const [choice, setChoice] = useState<RoleChoice | null>(null);
  const [notice, setNotice] = useState<'invited' | 'resent' | 'revoked' | 'offboarded' | null>(null);
  const [offboarding, setOffboarding] = useState<StaffMember | null>(null);
  const offboardForm = useForm<OffboardForm>({ resolver: zodResolver(offboardSchema), defaultValues: { reason: '' } });
  const form = useForm<AddStaffForm>({ resolver: zodResolver(addStaffSchema), defaultValues: { email: '', facilityId: defaultFacilityId } });

  const close = () => { setStep(null); setChoice(null); form.reset({ email: '', facilityId: defaultFacilityId }); };
  const add = useMutation({
    mutationFn: (values: AddStaffForm) => {
      if (!choice || choice.kind === 'doctor') throw new Error('no role');
      const role = choice.kind === 'builtIn' ? choice.role : (roles.data?.custom.find(c => c.id === choice.id)?.baseRole as AssignableRole);
      return addStaff({ ...values, role, customRoleId: choice.kind === 'custom' ? choice.id : null });
    },
    onSuccess: () => { close(); setNotice('invited'); refresh(); },
  });
  const resend = useMutation({ mutationFn: resendInvitation, onSuccess: () => { setNotice('resent'); refresh(); } });
  const revoke = useMutation({ mutationFn: revokeInvitation, onSuccess: () => { setNotice('revoked'); refresh(); } });

  const offboard = useMutation({
    mutationFn: (values: OffboardForm) => offboardStaff(offboarding!.membershipId, values.reason),
    onSuccess: () => { closeOffboard(); setNotice('offboarded'); refresh(); },
  });
  const closeOffboard = () => { setOffboarding(null); offboardForm.reset({ reason: '' }); offboard.reset(); };

  // Doctors need a licence check, so choosing one stops the steps and shows where to go instead.
  const canContinue = step === 'role' ? choice !== null && choice.kind !== 'doctor' : true;
  const next = async () => {
    if (step === 'role' && canContinue) setStep('scope');
    else if (step === 'scope' && (await form.trigger())) setStep('permissions');
  };

  return {
    staff: staff.data ?? [],
    loading: staff.isPending || roles.isPending || locations.isPending,
    failed: staff.isError || roles.isError || locations.isError,
    catalogue: roles.data ?? { builtIn: [], custom: [] },
    locations: locations.data ?? [],
    step,
    choice,
    form,
    canContinue,
    notice,
    adding: add.isPending,
    addFailed: add.isError,
    working: resend.isPending || revoke.isPending,
    rowFailed: resend.isError || revoke.isError,
    onOpen: () => { setNotice(null); setStep('role'); },
    onChoice: setChoice,
    onNext: () => void next(),
    onBack: () => setStep(step === 'permissions' ? 'scope' : 'role'),
    onClose: close,
    onSubmit: form.handleSubmit(values => add.mutate(values)),
    offboarding,
    offboardForm,
    offboardWorking: offboard.isPending,
    offboardError: !offboard.isError ? null : offboard.error instanceof ApiError && offboard.error.code === 'membershipId:last_admin' ? ('lastAdmin' as const) : ('failed' as const),
    onOffboard: (m: StaffMember) => { setNotice(null); offboard.reset(); setOffboarding(m); },
    onOffboardSubmit: offboardForm.handleSubmit(values => offboard.mutate(values)),
    onOffboardCancel: closeOffboard,
    onResend: (id: string) => { setNotice(null); resend.mutate(id); },
    onRevoke: (id: string) => { setNotice(null); revoke.mutate(id); },
  };
}
