import { useEffect, useState } from 'react';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { emptyLocation, locationSchema, resourceSchema, type LocationForm, type ResourceForm } from '../domain/settings';
import { addLocation, addResource, fetchTemplates, saveLocation } from '../services/settingsApi';
import { useLocationChoice } from './useLocationChoice';

/** Locations tab: add or edit a site, and the rooms or resources (such as a procedure room) bookable at it. */
export function useLocationsController(defaultId: string) {
  const queryClient = useQueryClient();
  const choice = useLocationChoice(defaultId);
  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['settings', 'locations'] });

  // The form edits one location, or adds a new one when none is being edited.
  const [editingId, setEditingId] = useState<string | null>(null);
  const form = useForm<LocationForm>({ resolver: zodResolver(locationSchema), defaultValues: emptyLocation });
  const save = useMutation({
    mutationFn: (values: LocationForm) => (editingId ? saveLocation(editingId, values) : addLocation(values)),
    onSuccess: () => { setEditingId(null); form.reset(emptyLocation); refresh(); },
  });
  const edit = (id: string | null) => {
    setEditingId(id);
    const l = choice.locations.find(x => x.id === id);
    form.reset(l ? { name: l.name, address: l.address ?? '', timeZone: l.timeZone } : emptyLocation);
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
    saveFailed: save.isError,
    onSubmit: form.handleSubmit(values => save.mutate(values)),
    resources: (calendars.data ?? []).filter(c => c.calendarKind === 'Resource'),
    resourcesFailed: calendars.isError,
    resourceForm,
    addingResource: addRoom.isPending,
    addResourceFailed: addRoom.isError,
    onAddResource: resourceForm.handleSubmit(values => addRoom.mutate(values)),
  };
}
