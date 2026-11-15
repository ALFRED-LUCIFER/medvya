import { Download } from 'lucide-react';
import { useI18n, type MessageKey } from '@medvya/i18n';
import { Button, PageContainer, PageHeader, Table, TableBody, TableCell, TableHead, TableHeader, TableRow, TableState, StickyActionBar } from '@medvya/ui';
import ReasonPanel, { ReasonBar } from '../../shell/components/ReasonPanel';
import type { FormEventHandler } from 'react';
import type { UseFormReturn } from 'react-hook-form';
import { actionKey, auditColumns, documentKindOf, isKnownAction, type AuditEntry } from '../domain/audit';
import { isDocumentKind } from '../../approvals/domain/approvals';
import type { ReasonForm } from '../../team/domain/team';

type Props = {
  vm: {
    state: 'loading' | 'error' | 'empty' | 'ready'; forbidden: boolean; entries: AuditEntry[];
    hasMore: boolean; loadingMore: boolean; onLoadMore: () => void;
    canExport: boolean; exporting: boolean; form: UseFormReturn<ReasonForm>; exportBusy: boolean; exportFailed: boolean;
    onStartExport: () => void; onCancelExport: () => void; onExport: FormEventHandler;
  };
};

/** Platform audit log (design: Medvya Admin, audit log): append-only, exportable for auditors. */
export default function AuditPage({ vm }: Props) {
  const { t, formatDate, formatTime } = useI18n();
  // Which document: its kind in words; a kind added on the server before its words shows as recorded.
  const detailText = (detail: string) => { const kind = documentKindOf(detail); return isDocumentKind(kind) ? t(`platform.approvals.doc.${kind}` as MessageKey) : kind; };
  return (
    <PageContainer>
      <PageHeader title={t('platform.audit.title')} description={t('platform.audit.lead')} />
      {vm.exporting && (
        <ReasonPanel title={t('platform.audit.exportTitle')} form={vm.form} onConfirm={vm.onExport}>
          <p className="text-sm">{t('platform.audit.exportBody')}</p>
        </ReasonPanel>
      )}
      <TableState state={vm.state} messages={{ loading: t('platform.state.loading'), error: t(vm.forbidden ? 'platform.state.forbidden' : 'platform.state.error'), empty: t('platform.audit.empty') }}>
        {/* Stacked under 768 px with the time first, so nothing scrolls sideways on a phone (MDV-P2-054). */}
        <Table label={t('platform.audit.title')} stacked>
          <TableHeader>
            <TableRow>{auditColumns.map(c => <TableHead key={c} scope="col">{t(c)}</TableHead>)}</TableRow>
          </TableHeader>
          <TableBody>
            {vm.entries.map(a => (
              <TableRow key={a.id}>
                <TableHead scope="row"><time dateTime={a.occurredAt}>{formatDate(new Date(a.occurredAt))} {formatTime(new Date(a.occurredAt))}</time></TableHead>
                <TableCell label={t('platform.audit.what')}>
                  {isKnownAction(a.action) ? t(actionKey(a.action)) : a.action}
                  {/* Which organisation and which document (F-037). */}
                  {(a.subjectName || a.detail) && (
                    <span className="block text-sm text-muted-foreground">
                      {a.subjectName && <bdi>{a.subjectName}</bdi>}{a.subjectName && a.detail && ' · '}{a.detail && detailText(a.detail)}
                    </span>
                  )}
                </TableCell>
                <TableCell label={t('platform.audit.who')}>
                  {a.actor ?? t('platform.audit.system')}
                  {a.actorEmail && <span dir="ltr" className="block text-sm break-all text-muted-foreground">{a.actorEmail}</span>}
                </TableCell>
                <TableCell label={t('platform.audit.reason')}>{a.reason ?? t('platform.audit.noReason')}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableState>
      {vm.hasMore && <Button variant="outline" className="self-start" onClick={vm.onLoadMore} loading={vm.loadingMore}>{t('platform.audit.more')}</Button>}
      {vm.exporting ? (
        <ReasonBar confirmLabel={t('platform.audit.exportConfirm')} busy={vm.exportBusy} problem={vm.exportFailed ? t('platform.error.failed') : null} onCancel={vm.onCancelExport} />
      ) : vm.canExport && (
        <StickyActionBar>
          <Button variant="outline" onClick={vm.onStartExport}><Download aria-hidden />{t('platform.audit.export')}</Button>
        </StickyActionBar>
      )}
    </PageContainer>
  );
}
