import { Ban, Check, Eye, MessageSquareWarning, Play, X } from 'lucide-react';
import type { FormEventHandler } from 'react';
import type { UseFormReturn } from 'react-hook-form';
import { useI18n, type MessageKey } from '@medvya/i18n';
import { Button, Card, ChoiceRow, EmptyState, StickyActionBar, Table, TableBody, TableCell, TableHead, TableHeader, TableRow, TableState } from '@medvya/ui';
import ReasonPanel, { ReasonBar } from '../../shell/components/ReasonPanel';
import type { ReasonForm } from '../../team/domain/team';
import { documentColumns, historyColumns, isDestructive, isDocumentKind, sizeLabel, taxCheck, type Action, type Detail, type DocumentView, type EmailPreview } from '../domain/approvals';
import DocumentViewer from './DocumentViewer';

export type DetailVm = {
  selected: boolean; state: 'loading' | 'error' | 'ready'; notFound: boolean; detail: Detail | null;
  canDecide: boolean; allowed: readonly Action[]; blockedByDpa: boolean;
  checked: readonly string[]; opened: readonly string[]; canTick: boolean; blockedByChecks: boolean; onCheck: (id: string, on: boolean) => void;
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
  if (!vm.selected) return <EmptyState id="approvals-detail">{t('platform.approvals.choose')}</EmptyState>;
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

      <Card size="item" title={t('platform.approvals.profile.title')}>
        <dl className="grid gap-x-6 gap-y-2 md:grid-cols-2">
          {profile.map(([label, value]) => (
            <div key={label}><dt className="text-sm text-muted-foreground">{t(label)}</dt><dd dir="auto" className="font-semibold break-words">{value ?? t('platform.approvals.notGiven')}</dd></div>
          ))}
        </dl>
        <p className="text-sm font-semibold">{t(`platform.approvals.check.tax.${tax}` as MessageKey)}</p>
      </Card>

      <section aria-labelledby="approvals-docs" className="flex flex-col gap-2">
        <h3 id="approvals-docs" className="text-base font-bold">{t('platform.approvals.docs.title')}</h3>
        <p className="text-sm text-muted-foreground">{t('platform.approvals.docs.manual')}</p>
        {/* Stacked under 768 px with the document first, so nothing scrolls sideways on a phone (MDV-P2-054). */}
        <Table label={t('platform.approvals.docs.tableLabel')} stacked>
          <TableHeader>
            <TableRow>
              {documentColumns.map(c => <TableHead key={c} scope="col">{t(c)}</TableHead>)}
              {vm.canTick && <TableHead scope="col">{t('platform.approvals.docs.checked')}</TableHead>}
              <TableHead scope="col"><span className="sr-only">{t('platform.approvals.docs.open')}</span></TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {d.documents.map(doc => {
              const name = isDocumentKind(doc.kind) ? t(`platform.approvals.doc.${doc.kind}` as MessageKey) : doc.kind;
              const unopened = !vm.opened.includes(doc.id);
              return (
                <TableRow key={doc.id}>
                  <TableHead scope="row" className="font-semibold">{name}</TableHead>
                  <TableCell label={t('platform.approvals.docs.checks')}>{t('platform.approvals.check.format', { type: doc.contentType === 'application/pdf' ? 'PDF' : doc.contentType.replace('image/', '').toUpperCase(), size: sizeLabel(doc.sizeBytes) })}</TableCell>
                  {vm.canTick && (
                    <TableCell label={t('platform.approvals.docs.checked')}>
                      {/* A document is ticked only after it was opened; until then the box is off and says why. */}
                      <ChoiceRow type="checkbox" checked={vm.checked.includes(doc.id)} disabled={unopened} onChange={e => vm.onCheck(doc.id, e.target.checked)}
                        aria-label={t('platform.approvals.docs.checkNamed', { name })} aria-describedby={unopened ? `approvals-open-first-${doc.id}` : undefined}>{t('platform.approvals.docs.checked')}</ChoiceRow>
                      {unopened && <p id={`approvals-open-first-${doc.id}`} className="text-sm text-muted-foreground">{t('platform.approvals.docs.openFirst')}</p>}
                    </TableCell>
                  )}
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

      {/* The decision buttons are in the page's bottom bar (DecisionBar); the reason and the sent email show here. */}
      {(vm.pending || vm.sent) && <section aria-labelledby="approvals-decision" className="flex flex-col gap-3">
        <h3 id="approvals-decision" className="text-base font-bold">{t('platform.approvals.decision.title')}</h3>
        {vm.pending && (
          <ReasonPanel title={t(`platform.approvals.confirm.${vm.pending}.title` as MessageKey)} form={vm.form} onConfirm={vm.onConfirm}>
            <p className="text-sm">{t(`platform.approvals.confirm.${vm.pending}.body` as MessageKey)}</p>
          </ReasonPanel>
        )}
        {vm.sent && (
          <Card size="item" role="status">
            <p className="font-semibold">{t('platform.approvals.sent.done')}</p>
            <p className="text-sm text-muted-foreground">{t('platform.approvals.sent.preview')}</p>
            <p className="font-semibold">{vm.sent.subject}</p>
            <p className="text-sm whitespace-pre-line">{vm.sent.body}</p>
            <Button variant="ghost" className="self-start" onClick={vm.onDismissSent}>{t('platform.link.done')}</Button>
          </Card>
        )}
      </section>}

      <section aria-labelledby="approvals-history" className="flex flex-col gap-2">
        <h3 id="approvals-history" className="text-base font-bold">{t('platform.approvals.history.title')}</h3>
        {d.history.length === 0 ? <p className="text-sm text-muted-foreground">{t('platform.approvals.history.empty')}</p> : (
          <Table label={t('platform.approvals.history.tableLabel')} stacked>
            <TableHeader>
              <TableRow>{historyColumns.map(c => <TableHead key={c} scope="col">{t(c)}</TableHead>)}</TableRow>
            </TableHeader>
            <TableBody>
              {d.history.map(h => (
                <TableRow key={`${h.at}-${h.state}`}>
                  <TableHead scope="row"><time dateTime={h.at}>{when(h.at)}</time></TableHead>
                  <TableCell label={t('platform.approvals.history.decision')}>{t(`platform.approvals.state.${h.state}` as MessageKey)}</TableCell>
                  <TableCell label={t('platform.audit.who')}>{h.actor ?? t('platform.approvals.history.applicant')}</TableCell>
                  <TableCell label={t('platform.audit.reason')}>{h.reason ?? t('platform.audit.noReason')}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </section>
    </>
  );
}

/** The decision for the open request, in the page's bottom bar (MDV-P2-065): why it cannot be decided at the start, the actions at the end, the filled one last. */
export function DecisionBar({ vm }: { vm: DetailVm }) {
  const { t } = useI18n();
  if (!vm.selected || vm.state !== 'ready' || !vm.detail) return null;
  if (vm.pending)
    return <ReasonBar confirmLabel={t(`platform.approvals.confirm.${vm.pending}.confirm` as MessageKey)} destructive={isDestructive(vm.pending)} busy={vm.busy} problem={vm.problem} onCancel={vm.onCancel} />;
  const actions = vm.canDecide ? [...vm.allowed].sort((a, b) => Number(isFilled(a)) - Number(isFilled(b))) : [];
  const status = !vm.canDecide ? t('platform.approvals.decision.readOnly')
    : vm.allowed.length === 0 ? t('platform.approvals.decision.none') : null;
  return (
    <StickyActionBar status={<>
      {status && <p className="text-sm text-muted-foreground">{status}</p>}
      {vm.canDecide && vm.allowed.includes('approve') && vm.blockedByDpa && <p id="approvals-dpa" className="text-sm font-semibold text-danger-700">{t('platform.approvals.error.dpa')}</p>}
      {vm.canDecide && vm.allowed.includes('approve') && !vm.blockedByDpa && vm.blockedByChecks && <p id="approvals-checks" className="text-sm text-muted-foreground">{t('platform.approvals.error.unchecked')}</p>}
    </>}>
      {actions.map(a => {
        const Icon = icons[a];
        const blocked = a === 'approve' && (vm.blockedByDpa || vm.blockedByChecks);
        return (
          // One filled button per view: Reject and Suspend are outlined in the danger tokens and say what they do in words and icon (F-052).
          <Button key={a} variant={isFilled(a) ? 'default' : 'outline'} className={isDestructive(a) ? 'border-danger-border text-danger-text' : undefined} disabled={blocked}
            aria-describedby={blocked ? (vm.blockedByDpa ? 'approvals-dpa' : 'approvals-checks') : undefined} onClick={() => vm.onStart(a)}>
            <Icon aria-hidden />{t(`platform.approvals.action.${a}` as MessageKey)}
          </Button>
        );
      })}
    </StickyActionBar>
  );
}

const isFilled = (a: Action) => a === 'approve' || a === 'reinstate';
