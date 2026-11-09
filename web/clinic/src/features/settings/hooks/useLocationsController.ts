import { useEffect, useState } from 'react';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import type { MessageKey } from '@medvya/i18n';
import { addLocationSchema, certificateErrorKey, certificateProblem, locationErrorKeys, emptyAddLocation, locationProblem, resourceSchema, type AddLocationForm, type ResourceForm } from '../domain/settings';
import { ApiError } from '../../schedule/services/scheduleApi';
import { addLocation, addResource, fetchTemplates, saveLocation } from '../services/settingsApi';
import { useLocationChoice } from './useLocationChoice';

/** Locations tab: add or edit a site, and the rooms or resources (such as a procedure room) bookable at it. */
/** The organisation's country is sent with a new location so the server can refuse one from another country (ADR-003). */
export function useLocationsController(defaultId: string, country: 'IN' | 'AE' | null) {
  const queryClient = useQueryClient();
  const choice = useLocationChoice(defaultId);
  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['settings', 'locations'] });

  // The form edits one location, or adds a new one when none is being edited.
  const [editingId, setEditingId] = useState<string | null>(null);
  const form = useForm<AddLocationForm>({ resolver: zodResolver(addLocationSchema), defaultValues: emptyAddLocation });
  // The registration certificate of a new location (a file is not form state).
  const [certificate, setCertificate] = useState<File | null>(null);
  const [certificateError, setCertificateError] = useState<MessageKey | null>(null);
  const [problem, setProblem] = useState<MessageKey | null>(null);
  const [added, setAdded] = useState<string | null>(null);
  const reset = (values: AddLocationForm) => { form.reset(values); setCertificate(null); setCertificateError(null); };
  const save = useMutation({
    mutationFn: async (values: AddLocationForm) => {
      if (editingId) return saveLocation(editingId, { name: values.name, address: values.address, timeZone: values.timeZone });
      return addLocation(values, certificate!, country ?? '');
    },
    onSuccess: l => { setAdded(editingId ? null : l.name); setEditingId(null); reset(emptyAddLocation); refresh(); },
    onError: e => {
      const p = locationProblem(e instanceof ApiError ? e.code : undefined);
      if (p.field === 'taxNumber') form.setError('taxNumber', { type: 'server', message: p.key });
      else if (p.field === 'certificate') setCertificateError(p.key);
      else setProblem(p.key);
    },
  });
  const edit = (id: string | null) => {
    setEditingId(id);
    setAdded(null);
    const l = choice.locations.find(x => x.id === id);
    reset(l ? { name: l.name, address: l.address ?? '', timeZone: l.timeZone, taxNumber: '' } : emptyAddLocation);
  };

  // Rooms and resources are scheduling calendars of the location.
  const calendars = useQuery({ queryKey: ['settings', 'templates', choice.locationId], queryFn: () => fetchTemplates(choice.locationId), enabled: !!choice.locationId });
  const resourceForm = useForm<ResourceForm>({ resolver: zodResolver(resourceSchema), defaultValues: { name: '' } });
  const addRoom = useMutation({
    mutationFn: (values: ResourceForm) => addResource(choice.locationId, values.name),
    onSuccess: () => { resourceForm.reset({ name: '' }); void queryClient.invalidateQueries({ queryKey: ['settings', 'templates'] }); },
  });
  useEffect(() => { resourceForm.reset({ name: '' }); }, [choice.locationId, resourceForm]);

  return {
    ...choice,
    form,
    editingId,
    onEdit: edit,
    saving: save.isPending,
    /** Beside the button: a wrong country, or any refusal that has no field of its own. */
    saveProblem: problem,
    certificateError,
    certificateName: certificate?.name ?? null,
    onCertificate: (file: File | undefined) => { setCertificate(file ?? null); setCertificateError(null); },
    added,
    onSubmit: form.handleSubmit(values => {
      setProblem(null);
      const missing = editingId ? null : certificateProblem(certificate);
      if (missing) setCertificateError(certificateErrorKey(missing));
      if (!editingId && !values.taxNumber) form.setError('taxNumber', { type: 'server', message: locationErrorKeys['taxNumber:required'] });
      else if (!missing) save.mutate(values);
    }),
    resources: (calendars.data ?? []).filter(c => c.calendarKind === 'Resource'),
    resourcesFailed: calendars.isError,
    resourceForm,
    addingResource: addRoom.isPending,
    addResourceFailed: addRoom.isError,
    onAddResource: resourceForm.handleSubmit(values => addRoom.mutate(values)),
  };
}
