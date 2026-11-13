import { useEffect, useRef } from 'react';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { useI18n } from '@medvya/i18n';
import { ApiError } from '../../schedule/services/scheduleApi';
import { notificationPrefsSchema, toPrefsForm, toPrefsPayload, type NotificationPrefsForm } from '../domain/notifications';
import { fetchNotificationPrefs, fetchTemplatePreviews, saveNotificationPrefs } from '../services/settingsApi';

const blank: NotificationPrefsForm = { confirmation: true, reminder24h: true, reminder2h: true, changes: true, email: true, sms: true, quietStart: '21:00', quietEnd: '08:00', senderName: '' };

/** Notifications tab: which messages go out, on which channels, the quiet hours, and the templates with placeholders. The server decides who may change them. */
export function useNotificationSettingsController() {
  const queryClient = useQueryClient();
  const { locale } = useI18n();
  const prefs = useQuery({ queryKey: ['settings', 'notifications'], queryFn: fetchNotificationPrefs });
  const previews = useQuery({ queryKey: ['settings', 'notification-templates', locale], queryFn: () => fetchTemplatePreviews(locale), staleTime: Infinity });
  const form = useForm<NotificationPrefsForm>({ resolver: zodResolver(notificationPrefsSchema), defaultValues: blank, shouldFocusError: false });
  const loaded = useRef<unknown>(null);
  useEffect(() => {
    if (prefs.data && loaded.current !== prefs.data) {
      loaded.current = prefs.data;
      form.reset(toPrefsForm(prefs.data));
    }
  }, [prefs.data, form]);
  const save = useMutation({
    mutationFn: (values: NotificationPrefsForm) => saveNotificationPrefs(toPrefsPayload(values)),
    onSuccess: data => queryClient.setQueryData(['settings', 'notifications'], data),
  });
  return {
    form,
    templates: previews.data ?? null,
    loading: prefs.isPending,
    forbidden: prefs.error instanceof ApiError && prefs.error.status === 403,
    loadFailed: prefs.isError,
    saving: save.isPending,
    saved: save.isSuccess && !form.formState.isDirty,
    // The server refuses a sender name with a web address in it (it goes out to patients) or one that is too long.
    senderNameError: save.error instanceof ApiError && save.error.code === 'senderName:link' ? ('link' as const)
      : save.error instanceof ApiError && save.error.code === 'senderName:too_long' ? ('tooLong' as const) : null,
    saveFailed: save.isError,
    onSubmit: form.handleSubmit(values => save.mutate(values)),
  };
}
