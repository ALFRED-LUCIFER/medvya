import { Download } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, PageContainer, PageHeader, Table, TableBody, TableCell, TableHead, TableHeader, TableRow, TableState } from '@medvya/ui';
import ReasonPanel from '../../shell/components/ReasonPanel';
import type { FormEventHandler } from 'react';
import type { UseFormReturn } from 'react-hook-form';
import { actionKey, auditColumns, isKnownAction, type AuditEntry } from '../domain/audit';
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
  return (
    <PageContainer>
      <PageHeader title={t('platform.audit.title')} description={t('platform.audit.lead')}
        actions={vm.canExport && <Button variant="outline" onClick={vm.onStartExport}><Download aria-hidden />{t('platform.audit.export')}</Button>} />
      {vm.exporting && (
        <ReasonPanel title={t('platform.audit.exportTitle')} form={vm.form} confirmLabel={t('platform.audit.exportConfirm')} busy={vm.exportBusy}
          problem={vm.exportFailed ? t('platform.error.failed') : null} onConfirm={vm.onExport} onCancel={vm.onCancelExport}>
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
                <TableCell label={t('platform.audit.what')}>{isKnownAction(a.action) ? t(actionKey(a.action)) : a.action}</TableCell>
                <TableCell label={t('platform.audit.who')}>{a.actor ?? t('platform.audit.system')}</TableCell>
                <TableCell label={t('platform.audit.reason')}>{a.reason ?? t('platform.audit.noReason')}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableState>
      {vm.hasMore && <Button variant="outline" className="self-start" onClick={vm.onLoadMore} loading={vm.loadingMore}>{t('platform.audit.more')}</Button>}
    </PageContainer>
  );
}
