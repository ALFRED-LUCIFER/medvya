import { useEffect, useRef, useState } from 'react';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { ApiError } from '../../schedule/services/scheduleApi';
import { logoProblem, profileSchema, settingsError, toProfileForm, type ProfileForm } from '../domain/settings';
import { fetchLogoLink, fetchProfile, removeLogo, saveProfile, uploadLogo } from '../services/settingsApi';


/** Profile tab: clinic details and logo. The server decides who may change them; this only follows its answer. */
export function useProfileController() {
  const queryClient = useQueryClient();
  const profile = useQuery({ queryKey: ['settings', 'profile'], queryFn: fetchProfile });
  const form = useForm<ProfileForm>({ resolver: zodResolver(profileSchema), defaultValues: toProfileForm({ name: '', address: null, registrationNumber: null, taxNumber: null, phone: null, email: null, hasLogo: false, version: 0 }), shouldFocusError: false });
  const loaded = useRef<unknown>(null);
  useEffect(() => {
    if (profile.data && loaded.current !== profile.data) {
      loaded.current = profile.data;
      form.reset(toProfileForm(profile.data));
    }
  }, [profile.data, form]);

  const save = useMutation({
    mutationFn: (values: ProfileForm) => saveProfile(values, profile.data?.version ?? 0),
    onSuccess: data => { queryClient.setQueryData(['settings', 'profile'], data); },
  });
  // The signed link expires in minutes; ask again whenever the logo changes or the screen is reopened.
  const link = useQuery({ queryKey: ['settings', 'logo', profile.data?.hasLogo], queryFn: fetchLogoLink, enabled: profile.data?.hasLogo === true, staleTime: 60_000 });

  const [pickProblem, setPickProblem] = useState<'type' | 'size' | null>(null);
  const afterLogoChange = () => void queryClient.invalidateQueries({ queryKey: ['settings'] });
  const upload = useMutation({ mutationFn: uploadLogo, onSuccess: afterLogoChange });
  const remove = useMutation({ mutationFn: removeLogo, onSuccess: afterLogoChange });

  const changed = save.error instanceof ApiError && save.error.status === 409 && save.error.code === 'changed';
  const rejected = upload.error instanceof ApiError ? settingsError(upload.error.code) : null;
  const logoError: 'type' | 'size' | 'failed' | null = pickProblem
    ?? (rejected === 'logo:type' ? 'type' : rejected === 'logo:too_large' ? 'size' : upload.isError || remove.isError ? 'failed' : null);

  return {
    form,
    loading: profile.isPending,
    forbidden: profile.error instanceof ApiError && profile.error.status === 403,
    loadFailed: profile.isError,
    saving: save.isPending,
    saved: save.isSuccess && !form.formState.isDirty,
    // #889: another administrator saved first; nothing was saved, and reloading shows what they saved.
    changed,
    saveFailed: save.isError && !changed,
    onReload: () => { save.reset(); void profile.refetch(); },
    // #886: a web address in the practice name is refused (the name goes into invitation emails).
    nameHasLink: save.error instanceof ApiError && settingsError(save.error.code) === 'name:link',
    onSubmit: form.handleSubmit(values => save.mutate(values)),
    logoUrl: profile.data?.hasLogo ? link.data?.url ?? null : null,
    hasLogo: profile.data?.hasLogo ?? false,
    clinicName: profile.data?.name ?? '',
    uploading: upload.isPending,
    removing: remove.isPending,
    /** Why the last logo was refused: picked locally, or by the server after checking the file itself. */
    logoError,
    onLogo: (file: File | undefined) => {
      if (!file) return;
      const problem = logoProblem(file);
      setPickProblem(problem);
      if (!problem) upload.mutate(file);
    },
    onRemoveLogo: () => { setPickProblem(null); remove.mutate(); },
  };
}
