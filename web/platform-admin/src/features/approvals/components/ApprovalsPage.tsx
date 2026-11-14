import { useI18n, type MessageKey } from '@medvya/i18n';
import { Button, PageContainer, PageHeader, Table, TableBody, TableCell, TableHead, TableHeader, TableRow, TableState, Tabs, TabsContent, TabsList, TabsTrigger, cn } from '@medvya/ui';
import { filters, queueColumns, waitDays, type Filter, type QueueItem } from '../domain/approvals';
import ApprovalDetail, { type DetailVm } from './ApprovalDetail';

export type QueueVm = {
  state: 'loading' | 'error' | 'empty' | 'ready'; forbidden: boolean; items: QueueItem[]; filter: Filter; selectedId: string | null; asOf: Date;
  onFilter: (f: Filter) => void; onSelect: (id: string) => void;
};

/** Organisation approvals (design: Medvya Admin, 10 Nov 2026): the queue on the left, the open request on the right. */
export default function ApprovalsPage({ vm, operator }: { vm: { queue: QueueVm; detail: DetailVm }; operator: string }) {
  const { t } = useI18n();
  const q = vm.queue;
  return (
    <PageContainer>
      <PageHeader title={t('platform.approvals.title')} description={t('platform.approvals.lead')} />
      <div className="grid items-start gap-4 lg:grid-cols-[minmax(0,340px)_minmax(0,1fr)]">
        <section aria-labelledby="approvals-queue" className="flex min-w-0 flex-col gap-3">
          <h2 id="approvals-queue" className="sr-only">{t('platform.approvals.queue')}</h2>
          <Tabs value={q.filter} onValueChange={v => q.onFilter(v as Filter)}>
            <TabsList variant="line">
              {filters.map(f => <TabsTrigger key={f} value={f} className="text-foreground">{t(`platform.approvals.filter.${f}` as MessageKey)}</TabsTrigger>)}
            </TabsList>
            <TabsContent value={q.filter}>
              <TableState state={q.state} messages={{ loading: t('platform.state.loading'), error: t(q.forbidden ? 'platform.state.forbidden' : 'platform.state.error'), empty: t('platform.approvals.empty') }}>
                {/* Stacked under 768 px with the organisation first, so nothing scrolls sideways on a phone (MDV-P2-054). */}
                <Table label={t('platform.approvals.queueTable')} stacked>
                  <TableHeader>
                    <TableRow>{queueColumns.map(c => <TableHead key={c} scope="col">{t(c)}</TableHead>)}</TableRow>
                  </TableHeader>
                  <TableBody>
                    {q.items.map(i => (
                      <TableRow key={i.tenantId} className={cn(q.selectedId === i.tenantId && 'bg-accent')}>
                        <TableHead scope="row" className="font-normal whitespace-normal">
                          <Button variant="link" className="h-auto justify-start px-0 text-start font-semibold whitespace-normal" aria-current={q.selectedId === i.tenantId ? 'true' : undefined}
                            onClick={() => q.onSelect(i.tenantId)}>{i.name}</Button>
                          <p className="text-sm text-muted-foreground">{t(`platform.approvals.type.${i.type}` as MessageKey)}{' · '}{i.country}</p>
                        </TableHead>
                        <TableCell label={t('platform.approvals.col.state')}>{t(`platform.approvals.state.${i.state}` as MessageKey)}</TableCell>
                        <TableCell label={t('platform.approvals.col.wait')}>{t('platform.approvals.days', { n: waitDays(i.submittedAt, q.asOf) })}</TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </TableState>
            </TabsContent>
          </Tabs>
        </section>
        <section aria-labelledby="approvals-detail" className="flex min-w-0 flex-col gap-4">
          <ApprovalDetail vm={vm.detail} operator={operator} />
        </section>
      </div>
    </PageContainer>
  );
}
