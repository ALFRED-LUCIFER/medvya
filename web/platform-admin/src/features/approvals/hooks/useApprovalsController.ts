import { useEffect, useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate, useParams } from 'react-router';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useI18n, type MessageKey } from '@medvya/i18n';
import { ApiError } from '../../api/services/api';
import { reasonForm, type ReasonForm } from '../../team/domain/team';
import type { PlatformRole } from '../../session/domain/session';
import { actionsFor, approveBlockedByDpa, canDecide, errorKeyOf, type Action, type DocumentView, type EmailPreview, type Filter } from '../domain/approvals';
import { decide, fetchDetail, fetchDocument, fetchQueue, release } from '../services/approvalsApi';

const queueKey = (filter: Filter) => ['platform', 'approvals', 'queue', filter] as const;
const detailKey = (id: string) => ['platform', 'approvals', 'detail', id] as const;

/** The approvals screen: the queue beside the open request (route /approvals/:tenantId), the private viewer and the decision with its reason. */
export function useApprovalsController(role: PlatformRole) {
  const { t } = useI18n();
  const client = useQueryClient();
  const navigate = useNavigate();
  const { tenantId } = useParams<{ tenantId: string }>();
  const [filter, setFilter] = useState<Filter>('Waiting');
  const [pending, setPending] = useState<Action | null>(null);
  const [sent, setSent] = useState<EmailPreview | null>(null);
  const [viewing, setViewing] = useState<{ doc: DocumentView; url: string; contentType: string; openedAt: Date } | null>(null);
  const [viewerFailed, setViewerFailed] = useState(false);
  const [opening, setOpening] = useState<string | null>(null);
  const reason = useForm<ReasonForm>({ resolver: zodResolver(reasonForm), defaultValues: { reason: '' }, shouldFocusError: false });

  const queue = useQuery({ queryKey: queueKey(filter), queryFn: () => fetchQueue(filter) });
  const detail = useQuery({ queryKey: detailKey(tenantId ?? ''), queryFn: () => fetchDetail(tenantId!), enabled: Boolean(tenantId) });

  // A viewed document is released when it is closed, replaced or the screen goes away; nothing stays in memory.
  const current = useRef<string | null>(null);
  useEffect(() => () => { if (current.current) release(current.current); }, []);
  const close = () => { if (current.current) release(current.current); current.current = null; setViewing(null); setViewerFailed(false); };

  const act = useMutation({
    mutationFn: ({ action, text }: { action: Action; text: string }) => decide(tenantId!, action, text),
    onSuccess: preview => {
      setPending(null); setSent(preview); reason.reset();
      void client.invalidateQueries({ queryKey: ['platform', 'approvals'] });
    },
  });
  const refused = act.error instanceof ApiError ? errorKeyOf(act.error.fields) : null;
  const detailError = act.error instanceof ApiError ? act.error : null;

  const d = detail.data;
  const allowed = d && canDecide(role) ? actionsFor(d.state) : [];
  const blockedByDpa = Boolean(d && approveBlockedByDpa(d));
  const problem: MessageKey | null = !act.isError ? null
    : refused === 'state' ? 'platform.approvals.error.state'
    : refused === 'dpa' ? 'platform.approvals.error.dpa'
    : detailError?.status === 404 ? 'platform.approvals.error.gone'
    : 'platform.error.failed';

  return {
    queue: {
      state: queue.isPending ? 'loading' as const : queue.isError ? 'error' as const : queue.data.length === 0 ? 'empty' as const : 'ready' as const,
      forbidden: queue.error instanceof ApiError && queue.error.status === 403,
      items: queue.data ?? [], filter, selectedId: tenantId ?? null,
      // Measured when the queue was read, so rendering stays pure.
      asOf: new Date(queue.dataUpdatedAt),
      onFilter: (f: Filter) => { setFilter(f); },
      onSelect: (id: string) => { setPending(null); setSent(null); close(); act.reset(); void navigate(`/approvals/${id}`); },
    },
    detail: {
      selected: Boolean(tenantId),
      state: detail.isPending ? 'loading' as const : detail.isError ? 'error' as const : 'ready' as const,
      notFound: detail.error instanceof ApiError && detail.error.status === 404,
      detail: d ?? null,
      canDecide: canDecide(role), allowed, blockedByDpa,
      pending, form: reason, busy: act.isPending, problem: problem ? t(problem) : null, sent,
      onStart: (action: Action) => {
        act.reset(); setSent(null); setPending(action);
        reason.reset({ reason: t(`platform.approvals.preset.${action}` as MessageKey) });
      },
      onCancel: () => { setPending(null); act.reset(); },
      onConfirm: reason.handleSubmit(({ reason: text }) => { if (pending) act.mutate({ action: pending, text }); }),
      onDismissSent: () => setSent(null),
      viewing, viewerFailed, opening,
      onOpen: async (doc: DocumentView) => {
        if (!tenantId) return;
        close(); setOpening(doc.id);
        try {
          const file = await fetchDocument(tenantId, doc.id);
          current.current = file.url;
          setViewing({ doc, ...file, openedAt: new Date() });
        } catch { setViewerFailed(true); } finally { setOpening(null); }
      },
      onClose: close,
    },
  };
}
