import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useParams } from 'react-router';
import { canDecide, decisionsFor, refusalKey, type Decision } from '../domain/organisations';
import { decide, documentLink, fetchDetail, openPendingTab } from '../services/organisationsApi';
import { reasonForm, type ReasonForm } from '../../team/domain/team';
import { ApiError } from '../../api/services/api';
import type { PlatformRole } from '../../session/domain/session';

export function useOrganisationDetailController(role: PlatformRole) {
  const { tenantId = '' } = useParams();
  const client = useQueryClient();
  const key = ['platform', 'organisation', tenantId] as const;
  const detail = useQuery({ queryKey: key, queryFn: () => fetchDetail(tenantId) });
  const [pending, setPending] = useState<Decision | null>(null);
  const [done, setDone] = useState<Decision | null>(null);
  const [documentFailed, setDocumentFailed] = useState(false);
  const form = useForm<ReasonForm>({ resolver: zodResolver(reasonForm), defaultValues: { reason: '' } });

  const act = useMutation({
    mutationFn: ({ decision, reason }: { decision: Decision; reason: string }) => decide(tenantId, decision, reason),
    onSuccess: (_, { decision }) => {
      setDone(decision); setPending(null); form.reset();
      void client.invalidateQueries({ queryKey: key });
      void client.invalidateQueries({ queryKey: ['platform', 'organisations'] });
    },
  });

  const onOpenDocument = (documentId: string) => {
    setDocumentFailed(false);
    const tab = openPendingTab();
    documentLink(tenantId, documentId).then(l => tab.go(l.url), () => { tab.close(); setDocumentFailed(true); });
  };

  const status = detail.error instanceof ApiError ? detail.error.status : null;
  return {
    state: detail.isPending ? 'loading' as const : detail.isError ? 'error' as const : 'ready' as const,
    forbidden: status === 403, notFound: status === 404,
    detail: detail.data ?? null,
    decisions: detail.data && canDecide(role) ? decisionsFor(detail.data.state) : [],
    pending, form, busy: act.isPending, done,
    problem: act.isError ? (act.error instanceof ApiError && act.error.status === 400 ? refusalKey(act.error.fields) : 'platform.error.failed' as const) : null,
    onStart: (d: Decision) => { act.reset(); form.reset(); setDone(null); setPending(d); },
    onCancel: () => setPending(null),
    onConfirm: form.handleSubmit(({ reason }) => { if (pending) act.mutate({ decision: pending, reason }); }),
    documentFailed, onOpenDocument,
  };
}
