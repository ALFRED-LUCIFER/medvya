import { useState } from 'react';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { ApiError } from '../../schedule/services/scheduleApi';
import { copyText } from '../../queue-tv/services/queueTvApi';
import { catalogueProblem } from '../domain/catalogue';
import { emptyRazorpayForm, razorpaySchema, toRazorpayBody, webhookUrl, type RazorpayForm } from '../domain/razorpay';
import { appOrigin, connectRazorpay, fetchRazorpay } from '../services/billingApi';

const key = ['billing', 'razorpay'];
const fields: readonly string[] = ['keyId', 'keySecret', 'webhookSecret'];

/** Settings, Billing, Razorpay (MDV-P4-007): the clinic pastes its own keys once; the screen shows only the mode, the key's last four and the webhook URL. */
export function useRazorpayController() {
  const queryClient = useQueryClient();
  const query = useQuery({ queryKey: key, queryFn: fetchRazorpay });
  const form = useForm<RazorpayForm>({ resolver: zodResolver(razorpaySchema), defaultValues: emptyRazorpayForm });
  const [open, setOpen] = useState(false);
  const [saveFailed, setSaveFailed] = useState(false);
  const [copied, setCopied] = useState(false);
  const close = () => { setOpen(false); setSaveFailed(false); form.reset(emptyRazorpayForm); }; // typed secrets are never kept
  const save = useMutation({
    mutationFn: (values: RazorpayForm) => connectRazorpay(toRazorpayBody(values)),
    onSuccess: data => { queryClient.setQueryData(key, data); close(); },
    onError: e => {
      const { field, code } = catalogueProblem(e instanceof ApiError ? e.code : undefined);
      if (field && fields.includes(field)) form.setError(field as keyof RazorpayForm, { type: 'server', message: code });
      else setSaveFailed(true);
    },
  });
  const url = webhookUrl(appOrigin(), query.data?.webhookPath ?? null);
  return {
    status: query.data ?? null,
    loading: query.isPending,
    loadFailed: query.isError,
    webhookUrl: url,
    copied,
    onCopy: () => { if (url) void copyText(url).then(() => setCopied(true), () => setCopied(false)); },
    form,
    open,
    saving: save.isPending,
    saveFailed,
    onOpen: () => setOpen(true),
    onClose: close,
    onSubmit: form.handleSubmit(values => save.mutate(values)),
  };
}
