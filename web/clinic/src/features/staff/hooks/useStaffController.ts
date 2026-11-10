import { useState } from 'react';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { fetchLocations } from '../../settings/services/settingsApi';
import { ApiError } from '../../schedule/services/scheduleApi';
import { addStaffSchema, offboardSchema, reactivateSchema, rebookingMissing, locationsOf, locationsSchema, reasonMissing, type LocationsForm, type AddStaffForm, type OffboardForm, type ReactivateForm, type StaffMember, type AssignableRole, type RoleChoice, type Step } from '../domain/staff';
import { addStaff, fetchRoles, fetchStaff, offboardStaff, reactivateStaff, resendInvitation, revokeInvitation, setStaffLocations } from '../services/staffApi';


/** Settings → Staff: the staff table and the "Add staff member" steps (role → location → permissions → invite). */
/** The invitation email could not be sent; the server saved nothing (502 with this code). */
/** The plan has no free doctor or staff seat (409 with this code); the server saved nothing. */
const planFull = (e: unknown) => e instanceof ApiError && e.code === 'plan_limit';
const sendFailed = (e: unknown) => e instanceof ApiError && e.code === 'email_failed';

export function useStaffController(defaultFacilityId: string) {
  const queryClient = useQueryClient();
  const staff = useQuery({ queryKey: ['staff', 'list'], queryFn: fetchStaff });
  const roles = useQuery({ queryKey: ['staff', 'roles'], queryFn: fetchRoles });
  const locations = useQuery({ queryKey: ['settings', 'locations'], queryFn: fetchLocations });
  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['staff'] }); // the list and this quarter's access review

  const [step, setStep] = useState<Step | null>(null);
  const [choice, setChoice] = useState<RoleChoice | null>(null);
  const [notice, setNotice] = useState<'invited' | 'resent' | 'revoked' | 'offboarded' | 'reactivated' | 'locations' | null>(null);
  /** Patients already checked in for the person just offboarded: reception reassigns them on the board. */
  const [openVisits, setOpenVisits] = useState(0);
  const [offboarding, setOffboarding] = useState<StaffMember | null>(null);
  const offboardForm = useForm<OffboardForm>({ resolver: zodResolver(offboardSchema), defaultValues: { reason: '', rebooking: null } });
  const [reactivating, setReactivating] = useState<StaffMember | null>(null);
  const reactivateForm = useForm<ReactivateForm>({ resolver: zodResolver(reactivateSchema), defaultValues: { reason: '' } });
  const form = useForm<AddStaffForm>({ resolver: zodResolver(addStaffSchema), defaultValues: { email: '', facilityId: defaultFacilityId, alsoFacilityIds: [] } });

  const close = () => { setStep(null); setChoice(null); form.reset({ email: '', facilityId: defaultFacilityId, alsoFacilityIds: [] }); };
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
    mutationFn: (values: OffboardForm) => offboardStaff(offboarding!.membershipId, values.reason, values.rebooking === 'offer'),
    onSuccess: result => { closeOffboard(); setOpenVisits(result.openVisits); setNotice('offboarded'); refresh(); },
  });
  const closeOffboard = () => { setOffboarding(null); offboardForm.reset({ reason: '', rebooking: null }); offboard.reset(); };
  const reactivate = useMutation({
    mutationFn: (values: ReactivateForm) => reactivateStaff(reactivating!.membershipId, values.reason),
    onSuccess: () => { closeReactivate(); setNotice('reactivated'); refresh(); },
  });
  const closeReactivate = () => { setReactivating(null); reactivateForm.reset({ reason: '' }); reactivate.reset(); };

  const [locating, setLocating] = useState<StaffMember | null>(null);
  const current = locating ? locationsOf(staff.data ?? [], locating) : [];
  const locationsForm = useForm<LocationsForm>({ resolver: zodResolver(locationsSchema), defaultValues: { facilityIds: [], reason: '' } });
  const closeLocations = () => { setLocating(null); locationsForm.reset({ facilityIds: [], reason: '' }); setLocations.reset(); };
  const setLocations = useMutation({
    mutationFn: (values: LocationsForm) => setStaffLocations(locating!.membershipId, values.facilityIds, values.reason),
    onSuccess: () => { closeLocations(); setNotice('locations'); refresh(); },
  });

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
    addFailed: add.isError && !sendFailed(add.error) && !planFull(add.error),
    planFull: planFull(add.error),
    emailFailed: sendFailed(add.error) || sendFailed(resend.error),
    working: resend.isPending || revoke.isPending,
    rowFailed: (resend.isError && !sendFailed(resend.error)) || revoke.isError,
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
    openVisits,
    onOffboard: (m: StaffMember) => { setNotice(null); closeReactivate(); offboard.reset(); setOffboarding(m); },
    onOffboardSubmit: offboardForm.handleSubmit(values => {
      if (offboarding && rebookingMissing(offboarding, values)) offboardForm.setError('rebooking', { type: 'required' });
      else offboard.mutate(values);
    }),
    onOffboardCancel: closeOffboard,
    reactivating,
    reactivateForm,
    reactivateWorking: reactivate.isPending,
    reactivateError: !reactivate.isError ? null : reactivate.error instanceof ApiError && reactivate.error.code === 'membershipId:above_granter' ? ('aboveGranter' as const) : planFull(reactivate.error) ? ('planFull' as const) : ('failed' as const),
    onReactivate: (m: StaffMember) => { setNotice(null); closeOffboard(); reactivate.reset(); setReactivating(m); },
    onReactivateSubmit: reactivateForm.handleSubmit(values => reactivate.mutate(values)),
    onReactivateCancel: closeReactivate,
    locating,
    locationsForm,
    locationsWorking: setLocations.isPending,
    locationsError: !setLocations.isError ? null : setLocations.error instanceof ApiError && setLocations.error.code === 'membershipId:last_admin' ? ('lastAdmin' as const) : ('failed' as const),
    onLocations: (m: StaffMember) => { setNotice(null); closeOffboard(); closeReactivate(); setLocations.reset(); locationsForm.reset({ facilityIds: locationsOf(staff.data ?? [], m), reason: '' }); setLocating(m); },
    onLocationsSubmit: locationsForm.handleSubmit(values => {
      if (reasonMissing(current, values)) locationsForm.setError('reason', { type: 'required' });
      else setLocations.mutate(values);
    }),
    onLocationsCancel: closeLocations,
    onResend: (id: string) => { setNotice(null); resend.mutate(id); },
    onRevoke: (id: string) => { setNotice(null); revoke.mutate(id); },
  };
}
