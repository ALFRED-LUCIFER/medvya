import { Link } from 'react-router';
import { useI18n } from '@medvya/i18n';
import { Button, Table, TableBody, TableCell, TableHead, TableHeader, TableRow, TableState } from '@medvya/ui';
import { filters, queueColumns, type QueueFilter, type QueueItem } from '../domain/organisations';

type Props = {
  vm: { filter: QueueFilter; onFilter: (f: QueueFilter) => void; state: 'loading' | 'error' | 'empty' | 'ready'; forbidden: boolean; items: QueueItem[] };
};

/** Organisation approval queue (design: Medvya Admin, organisation approval; MDV-P1-064). Oldest submission first. */
export default function OrganisationQueuePage({ vm }: Props) {
  const { t, formatDate } = useI18n();
  return (
    <>
      <div>
        <h1 className="text-2xl font-extrabold">{t('platform.organisations.title')}</h1>
        <p className="text-sm text-muted-foreground">{t('platform.organisations.lead')}</p>
      </div>
      <div role="group" aria-label={t('platform.organisations.filterLabel')} className="flex flex-wrap gap-2">
        {filters.map(f => (
          <Button key={f} variant={vm.filter === f ? 'default' : 'outline'} aria-pressed={vm.filter === f} onClick={() => vm.onFilter(f)}>
            {t(`platform.organisations.filter.${f}`)}
          </Button>
        ))}
      </div>
      <TableState state={vm.state} messages={{ loading: t('platform.state.loading'), error: t(vm.forbidden ? 'platform.state.forbidden' : 'platform.state.error'), empty: t('platform.organisations.empty') }}>
        <Table>
          <TableHeader>
            <TableRow>{queueColumns.map(c => <TableHead key={c}>{t(`platform.organisations.column.${c}`)}</TableHead>)}</TableRow>
          </TableHeader>
          <TableBody>
            {vm.items.map(o => (
              <TableRow key={o.tenantId}>
                <TableCell>
                  <Link to={`/organisations/${o.tenantId}`} className="inline-flex min-h-11 items-center font-semibold text-primary underline-offset-4 hover:underline">{o.name}</Link>
                </TableCell>
                <TableCell>{t(`platform.organisations.type.${o.type}`)}</TableCell>
                <TableCell>{o.country}</TableCell>
                <TableCell className="whitespace-nowrap">{o.submittedAt ? formatDate(new Date(o.submittedAt)) : t('platform.organisations.notSubmitted')}</TableCell>
                <TableCell>{t(`platform.organisations.state.${o.state}`)}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableState>
    </>
  );
}
