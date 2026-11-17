import { useEffect, useState } from 'react';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { ApiError } from '../../api/services/apiClient';
import { billingSettingsSchema, toSettingsBody, toSettingsForm, type BillingSettingsForm } from '../domain/billingSettings';
import { catalogueProblem } from '../domain/catalogue';
import { fetchBillingSettings, saveBillingSettings } from '../services/billingApi';

const key = ['billing', 'settings'];
const fields: readonly string[] = ['invoicePrefix', 'receiptPrefix', 'creditNotePrefix', 'paymentMethods', 'discountReasons', 'maxDiscountPercent', 'paymentLinkExpiryMinutes'];

/** Settings, Billing (MDV-P4-002): prefixes, payment methods, discount reasons and limits. The server checks prefixes by the country's numbering rules. */
export function useBillingSettingsController() {
  const queryClient = useQueryClient();
  const query = useQuery({ queryKey: key, queryFn: fetchBillingSettings });
  const form = useForm<BillingSettingsForm>({ resolver: zodResolver(billingSettingsSchema) });
  const { reset } = form;
  useEffect(() => { if (query.data) reset(toSettingsForm(query.data)); }, [query.data, reset]);
  const [saveFailed, setSaveFailed] = useState<'changed' | 'failed' | null>(null);
  const save = useMutation({
    mutationFn: (values: BillingSettingsForm) => saveBillingSettings(toSettingsBody(values, query.data?.version ?? 0)),
    onSuccess: data => { setSaveFailed(null); queryClient.setQueryData(key, data); },
    onError: e => {
      const { field, code } = catalogueProblem(e instanceof ApiError ? e.code : undefined);
      if (field && fields.includes(field)) form.setError(field as keyof BillingSettingsForm, { type: 'server', message: code });
      else setSaveFailed(code === 'changed' ? 'changed' : 'failed');
    },
  });
  return {
    form,
    loading: query.isPending,
    loadFailed: query.isError,
    saving: save.isPending,
    saved: save.isSuccess && !form.formState.isDirty,
    saveFailed,
    onSubmit: form.handleSubmit(values => save.mutate(values)),
  };
}
