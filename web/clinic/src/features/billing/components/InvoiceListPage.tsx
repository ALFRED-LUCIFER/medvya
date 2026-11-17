import { Hourglass, X } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import {
  Button, Chip, Field, Input, PageContainer, PageHeader, PageMessage, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, StickyActionBar,
  Table, TableBody, TableCell, TableHead, TableHeader, TablePagination, TableRow, TableState,
} from '@medvya/ui';
import { PatientPicker, type PatientSearchView } from '../../schedule/components/BookingPanel';
import type { TableView } from '../../table/domain/tableSort';
import InvoiceStateChip from './InvoiceStateChip';
import {
  invoiceColumns, invoiceStates, isInvoiceSortable, type InvoiceColumn, type InvoiceProblem, type InvoiceRow, type InvoiceSortColumn, type InvoiceState,
} from '../domain/invoice';

type Props = {
  timeZone: string;
  state: InvoiceState | null; onState: (s: InvoiceState | null) => void;
  from: string; onFrom: (v: string) => void; to: string; onTo: (v: string) => void;
  patientId: string | null; patientName: string | null; patientSearch: PatientSearchView; onClearPatient: () => void;
  table: TableView<InvoiceRow, InvoiceSortColumn>; empty: boolean; status: 'loading' | 'error' | 'forbidden' | 'ready';
  creating: boolean; createError: InvoiceProblem | null; onCreate: () => void; onOpen: (id: string) => void;
};

const any = 'any';

/** /billing (P-06, MDV-P4-004): the location's bills with their state, filtered by state, day range and patient. */
export default function InvoiceListPage(p: Props) {
  const { t, formatMoney, formatDate } = useI18n();
  const col = (c: InvoiceColumn) => t(`billing.invoices.col.${c}`);
  if (p.status === 'forbidden')
    return <PageContainer><PageHeader title={t('billing.invoices.title')} /><PageMessage kind="denied" title={t('err.403')}>{t('billing.invoices.forbidden')}</PageMessage></PageContainer>;

  return (
    <PageContainer className="gap-5">
      <PageHeader title={t('billing.invoices.title')} description={t('billing.invoices.intro')} />
      <div className="grid gap-3 wide:grid-cols-3">
        <Select value={p.state ?? any} onValueChange={v => p.onState(v === any ? null : (v as InvoiceState))}>
          <Field id="invoices-state" label={t('billing.invoices.filter.state')}>
            <SelectTrigger className="w-full"><SelectValue /></SelectTrigger>
          </Field>
          <SelectContent>
            <SelectItem value={any}>{t('billing.invoices.filter.anyState')}</SelectItem>
            {invoiceStates.map(s => <SelectItem key={s} value={s}>{t(`billing.state.${s}`)}</SelectItem>)}
          </SelectContent>
        </Select>
        <Field id="invoices-from" label={t('billing.invoices.filter.from')}>
          <Input type="date" value={p.from} max={p.to || undefined} onChange={e => p.onFrom(e.target.value)} />
        </Field>
        <Field id="invoices-to" label={t('billing.invoices.filter.to')}>
          <Input type="date" value={p.to} min={p.from || undefined} onChange={e => p.onTo(e.target.value)} />
        </Field>
      </div>
      {p.patientId ? (
        <div className="flex flex-wrap items-center gap-2 rounded-lg border p-3">
          <span className="font-bold">{p.patientName ? t('billing.invoices.patientChosen', { name: p.patientName }) : t('billing.invoices.patientChosenNoName')}</span>
          <Button type="button" variant="outline" onClick={p.onClearPatient}><X aria-hidden />{t('billing.invoices.clearPatient')}</Button>
        </div>
      ) : <PatientPicker search={p.patientSearch} chosen="" registerPath={null} invalid={false} />}

      <TableState state={p.status === 'loading' || p.status === 'error' ? p.status : p.empty ? 'empty' : 'ready'}
        messages={{ loading: t('billing.invoices.loading'), error: t('billing.invoices.loadFailed'), empty: t('billing.invoices.empty') }}>
        <Table label={t('billing.invoices.tableLabel')} stacked status={p.table.status}>
          <TableHeader>
            <TableRow>
              {invoiceColumns.map(c => (
                <TableHead key={c} scope="col" {...(isInvoiceSortable(c) && { sort: p.table.sortOf(c), onSort: () => p.table.onSort(c) })}>{col(c)}</TableHead>
              ))}
            </TableRow>
          </TableHeader>
          <TableBody>
            {p.table.rows.map(r => (
              <TableRow key={r.id}>
                <TableHead scope="row">
                  <Button variant="link" className="h-auto p-0 text-start" onClick={() => p.onOpen(r.id)}>
                    {r.number ? <bdi dir="ltr">{r.number}</bdi> : t('billing.invoices.noNumber')}
                  </Button>
                </TableHead>
                <TableCell label={col('patient')}>{r.patientName}<span className="block text-sm text-muted-foreground">{t('billing.invoices.mrn', { mrn: r.mrn })}</span></TableCell>
                <TableCell label={col('state')}>
                  <span className="flex flex-wrap gap-1">
                    <InvoiceStateChip state={r.state} />
                    {r.awaitingProvider && <Chip tone="info" icon={Hourglass} label={t('billing.payment.pending')} />}
                  </span>
                </TableCell>
                <TableCell label={col('total')}>{formatMoney(r.totalMinor, r.currency)}</TableCell>
                <TableCell label={col('paid')}>{formatMoney(r.paidMinor, r.currency)}</TableCell>
                <TableCell label={col('created')}><time dateTime={r.createdAt}>{formatDate(new Date(r.createdAt), p.timeZone)}</time></TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
        <TablePagination page={p.table.page} pageCount={p.table.pageCount} onPage={p.table.onPage} />
      </TableState>

      {p.patientId && (
        <StickyActionBar status={p.createError && <p role="alert" className="text-sm font-bold text-danger-700">{t(`billing.error.${p.createError}`)}</p>}>
          <Button loading={p.creating} onClick={p.onCreate}>{p.patientName ? t('billing.invoices.newFor', { name: p.patientName }) : t('billing.invoices.new')}</Button>
        </StickyActionBar>
      )}
    </PageContainer>
  );
}
