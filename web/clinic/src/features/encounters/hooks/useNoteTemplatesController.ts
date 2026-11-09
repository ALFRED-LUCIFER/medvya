import { useEffect } from 'react';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { templateChoiceSchema, type TemplateChoiceForm } from '../domain/note';
import { chooseNoteTemplates, fetchNoteCatalog } from '../services/noteApi';

/** Which of the six templates this doctor works with (MDV-P2-025). Their choice decides what the note editor offers when switching. */
export function useNoteTemplatesController() {
  const queryClient = useQueryClient();
  const catalog = useQuery({ queryKey: ['note-templates'], queryFn: fetchNoteCatalog, retry: false });
  const form = useForm<TemplateChoiceForm>({ resolver: zodResolver(templateChoiceSchema), defaultValues: { keys: [] } });
  useEffect(() => { if (catalog.data) form.reset({ keys: catalog.data.mine }); }, [catalog.data, form]);
  const save = useMutation({
    mutationFn: (v: TemplateChoiceForm) => chooseNoteTemplates(v.keys),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['note-templates'] }),
  });
  return {
    form,
    templates: catalog.data?.templates ?? [],
    loading: catalog.isPending,
    loadFailed: catalog.isError,
    saving: save.isPending,
    saved: save.isSuccess && !form.formState.isDirty,
    saveFailed: save.isError,
    onSubmit: form.handleSubmit(v => save.mutate(v)),
  };
}
