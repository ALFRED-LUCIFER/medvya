import { Download } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, Table, TableBody, TableCell, TableHead, TableHeader, TableRow, TableState } from '@medvya/ui';
import ReasonPanel from '../../shell/components/ReasonPanel';
import type { FormEventHandler } from 'react';
import type { UseFormReturn } from 'react-hook-form';
import { actionKey, isKnownAction, type AuditEntry } from '../domain/audit';
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
    <>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-2xl font-extrabold">{t('platform.audit.title')}</h1>
          <p className="text-sm text-muted-foreground">{t('platform.audit.lead')}</p>
        </div>
        {vm.canExport && <Button variant="outline" onClick={vm.onStartExport}><Download aria-hidden />{t('platform.audit.export')}</Button>}
      </div>
      {vm.exporting && (
        <ReasonPanel title={t('platform.audit.exportTitle')} form={vm.form} confirmLabel={t('platform.audit.exportConfirm')} busy={vm.exportBusy}
          problem={vm.exportFailed ? t('platform.error.failed') : null} onConfirm={vm.onExport} onCancel={vm.onCancelExport}>
          <p className="text-sm">{t('platform.audit.exportBody')}</p>
        </ReasonPanel>
      )}
      <TableState state={vm.state} messages={{ loading: t('platform.state.loading'), error: t(vm.forbidden ? 'platform.state.forbidden' : 'platform.state.error'), empty: t('platform.audit.empty') }}>
        <Table label={t('platform.audit.title')}>
          <TableHeader>
            <TableRow>
              <TableHead>{t('platform.audit.when')}</TableHead>
              <TableHead>{t('platform.audit.what')}</TableHead>
              <TableHead>{t('platform.audit.who')}</TableHead>
              <TableHead>{t('platform.audit.reason')}</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {vm.entries.map(a => (
              <TableRow key={a.id}>
                <TableCell className="whitespace-nowrap">{formatDate(new Date(a.occurredAt))} {formatTime(new Date(a.occurredAt))}</TableCell>
                <TableCell>{isKnownAction(a.action) ? t(actionKey(a.action)) : a.action}</TableCell>
                <TableCell>{a.actor ?? t('platform.audit.system')}</TableCell>
                <TableCell>{a.reason ?? t('platform.audit.noReason')}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableState>
      {vm.hasMore && <Button variant="outline" className="self-start" onClick={vm.onLoadMore} loading={vm.loadingMore}>{t('platform.audit.more')}</Button>}
    </>
  );
}
