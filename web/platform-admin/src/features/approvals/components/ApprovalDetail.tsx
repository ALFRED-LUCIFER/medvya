import { Ban, Check, Eye, MessageSquareWarning, Play, X } from 'lucide-react';
import type { FormEventHandler } from 'react';
import type { UseFormReturn } from 'react-hook-form';
import { useI18n, type MessageKey } from '@medvya/i18n';
import { Button, Table, TableBody, TableCell, TableHead, TableHeader, TableRow, TableState } from '@medvya/ui';
import ReasonPanel from '../../shell/components/ReasonPanel';
import type { ReasonForm } from '../../team/domain/team';
import { isDestructive, isDocumentKind, sizeLabel, taxCheck, type Action, type Detail, type DocumentView, type EmailPreview } from '../domain/approvals';
import DocumentViewer from './DocumentViewer';

export type DetailVm = {
  selected: boolean; state: 'loading' | 'error' | 'ready'; notFound: boolean; detail: Detail | null;
  canDecide: boolean; allowed: readonly Action[]; blockedByDpa: boolean;
  pending: Action | null; form: UseFormReturn<ReasonForm>; busy: boolean; problem: string | null; sent: EmailPreview | null;
  onStart: (a: Action) => void; onCancel: () => void; onConfirm: FormEventHandler; onDismissSent: () => void;
  viewing: { doc: DocumentView; url: string; contentType: string; openedAt: Date } | null; viewerFailed: boolean; opening: string | null;
  onOpen: (d: DocumentView) => void; onClose: () => void;
};

const icons: Record<Action, typeof Check> = { approve: Check, 'request-changes': MessageSquareWarning, reject: X, suspend: Ban, reinstate: Play };

/** One request: profile, documents with their checks and the private viewer, the decision with its reason, and the history. */
export default function ApprovalDetail({ vm, operator }: { vm: DetailVm; operator: string }) {
  const { t, formatDate, formatTime } = useI18n();
  const when = (iso: string) => `${formatDate(new Date(iso))} ${formatTime(new Date(iso))}`;
  if (!vm.selected) return <p id="approvals-detail" className="rounded-lg border border-dashed border-border p-6 text-muted-foreground">{t('platform.approvals.choose')}</p>;
  if (vm.state !== 'ready' || !vm.detail)
    return <TableState state={vm.state === 'ready' ? 'error' : vm.state} messages={{ loading: t('platform.state.loading'), error: t(vm.notFound ? 'platform.approvals.error.gone' : 'platform.state.error'), empty: '' }}><></></TableState>;
  const d = vm.detail;
  const tax = taxCheck(d.country, d.taxNumber);
  const profile: [MessageKey, string | null][] = [
    ['platform.approvals.profile.address', d.address], ['platform.approvals.profile.contact', d.contactEmail], ['platform.approvals.profile.registration', d.registrationNumber],
    ['platform.approvals.profile.tax', d.taxNumber], ...(d.type === 'Hospital' ? [['platform.approvals.profile.it', [d.itContactName, d.itContactEmail].filter(Boolean).join(' · ') || null] as [MessageKey, string | null]] : []),
  ];

  return (
    <>
      <header className="flex flex-col gap-1">
        <h2 id="approvals-detail" className="text-xl font-extrabold">{d.name}</h2>
        <p className="flex flex-wrap gap-2 text-sm font-semibold">
          <span>{t(`platform.approvals.type.${d.type}` as MessageKey)}</span><span aria-hidden>{'·'}</span>
          <span>{t(`platform.approvals.state.${d.state}` as MessageKey)}</span><span aria-hidden>{'·'}</span><span>{d.country}</span>
        </p>
        <p className="text-sm text-muted-foreground">{t('platform.approvals.noPatientData')}{d.submittedAt && ` · ${t('platform.approvals.submitted', { when: when(d.submittedAt) })}`}</p>
        {d.reason && <p className="text-sm"><span className="font-semibold">{t('platform.approvals.lastReason')}</span> {d.reason}</p>}
      </header>

      <section aria-labelledby="approvals-profile" className="flex flex-col gap-2 rounded-lg border border-border bg-card p-4">
        <h3 id="approvals-profile" className="text-base font-bold">{t('platform.approvals.profile.title')}</h3>
        <dl className="grid gap-x-6 gap-y-2 sm:grid-cols-2">
          {profile.map(([label, value]) => (
            <div key={label}><dt className="text-sm text-muted-foreground">{t(label)}</dt><dd dir="auto" className="font-semibold break-words">{value ?? t('platform.approvals.notGiven')}</dd></div>
          ))}
        </dl>
        <p className="text-sm font-semibold">{t(`platform.approvals.check.tax.${tax}` as MessageKey)}</p>
      </section>

      <section aria-labelledby="approvals-docs" className="flex flex-col gap-2">
        <h3 id="approvals-docs" className="text-base font-bold">{t('platform.approvals.docs.title')}</h3>
        <p className="text-sm text-muted-foreground">{t('platform.approvals.docs.manual')}</p>
        <Table label={t('platform.approvals.docs.tableLabel')}>
          <TableHeader>
            <TableRow>
              <TableHead>{t('platform.approvals.docs.document')}</TableHead>
              <TableHead>{t('platform.approvals.docs.checks')}</TableHead>
              <TableHead><span className="sr-only">{t('platform.approvals.docs.open')}</span></TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {d.documents.map(doc => {
              const name = isDocumentKind(doc.kind) ? t(`platform.approvals.doc.${doc.kind}` as MessageKey) : doc.kind;
              return (
                <TableRow key={doc.id}>
                  <TableCell className="font-semibold">{name}</TableCell>
                  <TableCell>{t('platform.approvals.check.format', { type: doc.contentType === 'application/pdf' ? 'PDF' : doc.contentType.replace('image/', '').toUpperCase(), size: sizeLabel(doc.sizeBytes) })}</TableCell>
                  <TableCell>
                    <Button variant="outline" aria-label={t('platform.approvals.docs.openNamed', { name })} loading={vm.opening === doc.id} onClick={() => vm.onOpen(doc)}>
                      <Eye aria-hidden />{t('platform.approvals.docs.open')}
                    </Button>
                  </TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
        {vm.viewerFailed && <p role="alert" className="text-sm font-semibold text-danger-700">{t('platform.approvals.docs.failed')}</p>}
        {vm.viewing && <DocumentViewer file={vm.viewing} operator={operator} onClose={vm.onClose} />}
      </section>

      <section aria-labelledby="approvals-decision" className="flex flex-col gap-3">
        <h3 id="approvals-decision" className="text-base font-bold">{t('platform.approvals.decision.title')}</h3>
        {!vm.canDecide && <p className="text-sm text-muted-foreground">{t('platform.approvals.decision.readOnly')}</p>}
        {vm.canDecide && vm.allowed.length === 0 && <p className="text-sm text-muted-foreground">{t('platform.approvals.decision.none')}</p>}
        {vm.canDecide && vm.allowed.length > 0 && (
          <div className="flex flex-wrap gap-2">
            {vm.allowed.map(a => {
              const Icon = icons[a];
              const blocked = a === 'approve' && vm.blockedByDpa;
              return (
                <Button key={a} variant={isDestructive(a) ? 'destructive' : a === 'approve' || a === 'reinstate' ? 'default' : 'outline'} disabled={blocked}
                  aria-describedby={blocked ? 'approvals-dpa' : undefined} onClick={() => vm.onStart(a)}>
                  <Icon aria-hidden />{t(`platform.approvals.action.${a}` as MessageKey)}
                </Button>
              );
            })}
          </div>
        )}
        {vm.canDecide && vm.allowed.includes('approve') && vm.blockedByDpa && <p id="approvals-dpa" className="text-sm font-semibold text-danger-700">{t('platform.approvals.error.dpa')}</p>}
        {vm.pending && (
          <ReasonPanel title={t(`platform.approvals.confirm.${vm.pending}.title` as MessageKey)} form={vm.form} confirmLabel={t(`platform.approvals.confirm.${vm.pending}.confirm` as MessageKey)}
            destructive={isDestructive(vm.pending)} busy={vm.busy} problem={vm.problem} onConfirm={vm.onConfirm} onCancel={vm.onCancel}>
            <p className="text-sm">{t(`platform.approvals.confirm.${vm.pending}.body` as MessageKey)}</p>
          </ReasonPanel>
        )}
        {vm.sent && (
          <div role="status" className="flex flex-col gap-2 rounded-lg border border-border bg-card p-4">
            <p className="font-semibold">{t('platform.approvals.sent.done')}</p>
            <p className="text-sm text-muted-foreground">{t('platform.approvals.sent.preview')}</p>
            <p className="font-semibold">{vm.sent.subject}</p>
            <p className="text-sm whitespace-pre-line">{vm.sent.body}</p>
            <Button variant="ghost" className="self-start" onClick={vm.onDismissSent}>{t('platform.link.done')}</Button>
          </div>
        )}
      </section>

      <section aria-labelledby="approvals-history" className="flex flex-col gap-2">
        <h3 id="approvals-history" className="text-base font-bold">{t('platform.approvals.history.title')}</h3>
        {d.history.length === 0 ? <p className="text-sm text-muted-foreground">{t('platform.approvals.history.empty')}</p> : (
          <Table label={t('platform.approvals.history.tableLabel')}>
            <TableHeader>
              <TableRow>
                <TableHead>{t('platform.audit.when')}</TableHead>
                <TableHead>{t('platform.approvals.history.decision')}</TableHead>
                <TableHead>{t('platform.audit.who')}</TableHead>
                <TableHead>{t('platform.audit.reason')}</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {d.history.map(h => (
                <TableRow key={`${h.at}-${h.state}`}>
                  <TableCell className="whitespace-nowrap">{when(h.at)}</TableCell>
                  <TableCell>{t(`platform.approvals.state.${h.state}` as MessageKey)}</TableCell>
                  <TableCell>{h.actor ?? t('platform.approvals.history.applicant')}</TableCell>
                  <TableCell>{h.reason ?? t('platform.audit.noReason')}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </section>
    </>
  );
}
