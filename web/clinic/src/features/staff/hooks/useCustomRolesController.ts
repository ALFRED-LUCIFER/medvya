import { useState } from 'react';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import type { Capability } from '../../session/domain/session';
import { ApiError } from '../../schedule/services/scheduleApi';
import { roleSchema, type CustomRole, type RoleForm } from '../domain/staff';
import { createRole, updateRole } from '../services/staffApi';

const empty: RoleForm = { name: '', baseRole: 'Receptionist' };

/** Custom roles: a built-in role plus extra rows the server allows (never beyond practice admin). */
export function useCustomRolesController() {
  const queryClient = useQueryClient();
  const [editing, setEditing] = useState<CustomRole | 'new' | null>(null);
  const [extras, setExtras] = useState<Capability[]>([]);
  const form = useForm<RoleForm>({ resolver: zodResolver(roleSchema), defaultValues: empty });

  const save = useMutation({
    mutationFn: (values: RoleForm) => (editing && editing !== 'new' ? updateRole(editing.id, { ...values, capabilities: extras }) : createRole({ ...values, capabilities: extras })),
    onSuccess: () => { setEditing(null); void queryClient.invalidateQueries({ queryKey: ['staff', 'roles'] }); },
  });

  const open = (role: CustomRole | 'new') => {
    setEditing(role);
    setExtras(role === 'new' ? [] : role.capabilities);
    form.reset(role === 'new' ? empty : { name: role.name, baseRole: role.baseRole });
    save.reset();
  };

  return {
    editing,
    form,
    extras,
    saving: save.isPending,
    saveError: !save.error ? null : save.error instanceof ApiError && save.error.code === 'name:duplicate' ? ('duplicate' as const) : ('failed' as const),
    onOpen: open,
    onClose: () => setEditing(null),
    // A different base role has different addable rows, so the picks start again.
    onBase: (baseRole: RoleForm['baseRole']) => { form.setValue('baseRole', baseRole, { shouldDirty: true }); setExtras([]); },
    onToggle: (capability: Capability, on: boolean) => setExtras(cur => (on ? [...cur, capability] : cur.filter(c => c !== capability))),
    onSubmit: form.handleSubmit(values => save.mutate(values)),
  };
}
