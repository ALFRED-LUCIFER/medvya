import type { FormEventHandler } from 'react';
import type { UseFormReturn } from 'react-hook-form';
import { CircleAlert, CircleCheck, FileDown, Hourglass, TriangleAlert } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import {
  Banner, Button, Card, Detail, DetailList, Field, Input, PageContainer, PageHeader, PageMessage, StickyActionBar,
  Table, TableBody, TableCell, TableHead, TableHeader, TablePagination, TableRow, TableState, Textarea,
} from '@medvya/ui';
import type { Currency } from '../domain/catalogue';
import {
  isSessionSortable, sessionColumns, type CashSession, type CountForm, type DayCloseProblem, type OpenForm, type SessionColumn, type SessionSortColumn,
} from '../domain/dayClose';
import type { TableView } from '../../table/domain/tableSort';

type Props = {
  timeZone: string;
  status: 'loading' | 'error' | 'forbidden' | 'ready';
  currency: Currency; current: CashSession | null; awaiting: CashSession[]; confirmable: CashSession | null; denominations: number[]; problem: DayCloseProblem | null;
  openForm: UseFormReturn<OpenForm>; opening: boolean; onOpen: FormEventHandler<HTMLFormElement>;
  countForm: UseFormReturn<CountForm>; counted: number; variance: number; noteMissing: boolean; counting: boolean; onCount: FormEventHandler<HTMLFormElement>;
  confirming: boolean; onConfirm: () => void;
  table: TableView<CashSession, SessionSortColumn>; empty: boolean;
  reporting: string | null | undefined; onReport: (id: string) => void;
};

/** Day close (P-07, P-13, MDV-P4-012): the drawer's float, the takings by method, the count by note and coin, the variance and its confirmation. */
export default function DayClosePage(p: Props) {
  const { t, formatMoney, formatDate, formatTime, formatNumber } = useI18n();
  const money = (minor: number) => formatMoney(minor, p.currency);
  const when = (at: string | null) => (at ? t('billing.dayClose.when', { date: formatDate(new Date(at), p.timeZone), time: formatTime(new Date(at), p.timeZone) }) : '');
  const col = (c: SessionColumn) => t(`billing.dayClose.col.${c}`);
  const title = t('billing.dayClose.title');
  if (p.status !== 'ready')
    return (
      <PageContainer>
        <PageHeader title={title} />
        {p.status === 'loading' ? <PageMessage kind="busy">{t('billing.dayClose.loading')}</PageMessage>
          : <PageMessage kind={p.status === 'forbidden' ? 'denied' : 'error'} title={t(p.status === 'forbidden' ? 'err.403' : 'billing.dayClose.title')}>
              {t(p.status === 'forbidden' ? 'billing.invoices.forbidden' : 'billing.dayClose.loadFailed')}
            </PageMessage>}
      </PageContainer>
    );

  const s = p.current;
  const short = p.variance < 0;
  return (
    <PageContainer className="gap-5">
      <PageHeader title={title} description={t('billing.dayClose.intro')} />
      <div aria-live="polite">{p.problem && <Banner tone="danger" icon={CircleAlert} title={t(`billing.dayClose.error.${p.problem}`)} />}</div>

      {p.awaiting.map(a => (
        <Card key={a.id} title={t('billing.dayClose.awaitingTitle')}>
          <Banner tone="info" icon={Hourglass} title={t('billing.dayClose.awaiting', { name: a.countedByName ?? '', amount: money(a.varianceMinor ?? 0) })}>
            {t(a.mayConfirm ? 'billing.dayClose.mayConfirm' : 'billing.dayClose.askColleague')}
          </Banner>
          <DetailList>
            <Detail label={t('billing.dayClose.openedBy')}>{t('billing.dayClose.byAt', { name: a.openedByName, when: when(a.openedAt) })}</Detail>
            <Detail label={t('billing.dayClose.countedAt')}>{when(a.countedAt)}</Detail>
            <Detail label={t('billing.dayClose.counted')}>{money(a.countedMinor ?? 0)}</Detail>
            <Detail label={t('billing.dayClose.expected')}>{money(a.expectedMinor)}</Detail>
            <Detail label={t('billing.dayClose.note')}>{a.note ?? ''}</Detail>
          </DetailList>
        </Card>
      ))}

      {!s ? (
        <Card title={t('billing.dayClose.openTitle')} intro={t('billing.dayClose.openIntro')}>
          <form id="open-drawer" noValidate onSubmit={p.onOpen} className="grid gap-3 wide:grid-cols-3">
            <Field id="day-close-float" label={t('billing.dayClose.float')} hint={t('billing.dayClose.floatHint', { currency: p.currency })}
              error={p.openForm.formState.errors.float ? t('billing.error.format') : null}>
              <Input dir="ltr" inputMode="decimal" autoComplete="off" {...p.openForm.register('float')} />
            </Field>
          </form>
        </Card>
      ) : (
        <>
          <Card title={t('billing.dayClose.sessionTitle')}>
            <DetailList>
              <Detail label={t('billing.dayClose.openedBy')}>{t('billing.dayClose.byAt', { name: s.openedByName, when: when(s.openedAt) })}</Detail>
              <Detail label={t('billing.dayClose.float')}>{money(s.openingFloatMinor)}</Detail>
              {s.totals.length === 0 ? <Detail label={t('billing.dayClose.takings')}>{t('billing.dayClose.noPayments')}</Detail>
                : s.totals.map(x => (
                  <Detail key={x.method} label={t(`billing.method.${x.method}`)}>{t('billing.dayClose.methodTotal', { count: formatNumber(x.payments), amount: money(x.amountMinor) })}</Detail>
                ))}
              <Detail label={t('billing.dayClose.expected')}><strong>{money(s.expectedMinor)}</strong></Detail>
            </DetailList>
          </Card>

          <Card title={t('billing.dayClose.countTitle')} intro={t('billing.dayClose.countIntro')}>
            <form id="count-drawer" noValidate onSubmit={p.onCount} className="flex flex-col gap-4">
              <div className="grid grid-cols-2 gap-3 wide:grid-cols-5">
                {p.denominations.map(d => (
                  <Field key={d} id={`day-close-count-${d}`} label={t('billing.dayClose.denomination', { value: money(d) })}>
                    <Input dir="ltr" inputMode="numeric" autoComplete="off" {...p.countForm.register(`counts.${d}`)} />
                  </Field>
                ))}
              </div>
              <DetailList layout="inline">
                <Detail label={t('billing.dayClose.counted')}><strong>{money(p.counted)}</strong></Detail>
                <Detail label={t('billing.dayClose.expected')}>{money(s.expectedMinor)}</Detail>
              </DetailList>
              {p.variance === 0
                ? <Banner tone="success" icon={CircleCheck} title={t('billing.dayClose.matches')} />
                : <Banner tone="warning" icon={TriangleAlert}
                    title={t(short ? 'billing.dayClose.short' : 'billing.dayClose.over', { amount: money(Math.abs(p.variance)) })}>{t('billing.dayClose.varianceRule')}</Banner>}
              {p.variance !== 0 && (
                <Field id="day-close-note" label={t('billing.dayClose.note')} hint={t('billing.dayClose.noteHint')} required
                  error={p.noteMissing && p.countForm.formState.isSubmitted ? t('billing.error.required') : null}>
                  <Textarea maxLength={200} {...p.countForm.register('note')} />
                </Field>
              )}
            </form>
          </Card>

        </>
      )}

      <Card title={t('billing.dayClose.closedTitle')}>
        <TableState state={p.empty ? 'empty' : 'ready'} messages={{ loading: t('billing.dayClose.loading'), error: t('billing.dayClose.loadFailed'), empty: t('billing.dayClose.noneClosed') }}>
          <Table label={t('billing.dayClose.tableLabel')} stacked status={p.table.status}>
            <TableHeader>
              <TableRow>
                {sessionColumns.map(c => (
                  <TableHead key={c} scope="col" {...(isSessionSortable(c) && { sort: p.table.sortOf(c), onSort: () => p.table.onSort(c) })}>{col(c)}</TableHead>
                ))}
              </TableRow>
            </TableHeader>
            <TableBody>
              {p.table.rows.map(r => (
                <TableRow key={r.id}>
                  <TableHead scope="row">{r.closedAt && <time dateTime={r.closedAt}>{when(r.closedAt)}</time>}</TableHead>
                  <TableCell label={col('openedBy')}>{r.openedByName}</TableCell>
                  <TableCell label={col('expected')}>{money(r.expectedMinor)}</TableCell>
                  <TableCell label={col('counted')}>{money(r.countedMinor ?? 0)}</TableCell>
                  <TableCell label={col('variance')}>
                    {money(r.varianceMinor ?? 0)}
                    {r.note && <span className="block text-sm text-muted-foreground">{r.note}</span>}
                    {r.adjustments.length > 0 && <span className="block text-sm text-muted-foreground">{t('billing.dayClose.adjusted', { count: formatNumber(r.adjustments.length) })}</span>}
                  </TableCell>
                  <TableCell label={col('confirmedBy')}>{r.confirmedByName ?? t('billing.dayClose.noConfirmation')}</TableCell>
                  <TableCell label={col('report')}>
                    <Button variant="outline" loading={p.reporting === r.id} onClick={() => p.onReport(r.id)}
                      aria-label={t('billing.dayClose.reportFor', { when: when(r.closedAt) })}>
                      <FileDown aria-hidden />{t('billing.dayClose.report')}
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          <TablePagination page={p.table.page} pageCount={p.table.pageCount} onPage={p.table.onPage} />
        </TableState>
      </Card>

      <StickyActionBar>
        {/* One primary: confirming a colleague's count first, else the drawer's own step. */}
        {!s ? <Button type="submit" form="open-drawer" variant={p.confirmable ? 'outline' : 'default'} loading={p.opening}>{t('billing.dayClose.open')}</Button>
          : <Button type="submit" form="count-drawer" variant={p.confirmable ? 'outline' : 'default'} loading={p.counting} disabled={p.noteMissing || p.confirming}>
              {t(p.variance === 0 ? 'billing.dayClose.close' : 'billing.dayClose.sendForConfirmation')}
            </Button>}
        {p.confirmable && <Button loading={p.confirming} disabled={p.counting} onClick={p.onConfirm}>{t('billing.dayClose.confirm')}</Button>}
      </StickyActionBar>
    </PageContainer>
  );
}
