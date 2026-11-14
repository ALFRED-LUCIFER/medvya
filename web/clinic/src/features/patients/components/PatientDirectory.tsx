import { useRef } from 'react';
import { Link } from 'react-router';
import { ChevronLeft, ChevronRight, CircleAlert, Info, Search, TriangleAlert, UserPlus } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, EmptyState, Input, Label, PageContainer, PageHeader, PageMessage, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Table, TableBody, TableCell, TableHead,
  TableHeader, TableRow, Tag, Tooltip, buttonVariants, cn } from '@medvya/ui';
import { directoryColumns, directoryFilters, pageCount, spokenExtras, type DirectoryFilter, type PatientSummary, type SearchErrorCode, type SearchResult } from '../domain/directory';

type Props = {
  text: string;
  onTextChange: (text: string) => void;
  onSubmit: () => void;
  filter: DirectoryFilter;
  onFilterChange: (filter: DirectoryFilter) => void;
  searched: boolean;
  onPageChange: (page: number) => void;
  result: SearchResult | null;
  /** True while a new page, filter or search loads; the previous results stay on screen (marked busy) so focus is never lost. */
  updating: boolean;
  state: 'loading' | 'ready' | 'invalid' | 'forbidden' | 'notFound' | 'error';
  errorCode: SearchErrorCode | null;
  onRetry: () => void;
  /** Path of the registration screen, when this membership may register (header button and empty state link). */
  registerPath: string | null;
  /** Merge review (P-03); null when this role has no part in it. */
  mergePath: string | null;
  recordPath: (id: string) => string;
};

/** Directory and privacy-safe search (MDV-P1-018, D-02 rules): initials, age, sex and MRN tail until a record is opened. */
export default function PatientDirectory(p: Props) {
  const { t } = useI18n();
  const hintId = 'patient-search-hint', errorId = 'patient-search-error';
  const invalid = p.state === 'invalid';
  // One live region that stays mounted; only its text changes, so screen readers announce every change.
  const status = p.state === 'loading' || p.updating ? t('patients.loading') : p.state === 'forbidden' ? t('err.403')
    : p.state === 'error' || p.state === 'notFound' ? t('patients.error.title')
    : p.state === 'ready' && p.result ? (p.result.items.length === 0 ? t('patients.empty') : t(`patients.matchedOn.${p.result.matchedOn}`, { n: p.result.total }))
    : '';
  return (
    <PageContainer width="reading">
      {/* Register patient is here and on Front desk, not in the menu (MDV-P2-051); outlined, as Search is this view's primary button. */}
      <PageHeader title={t('patients.directory.title')}
        actions={<>
          {p.mergePath && <Link to={p.mergePath} className={buttonVariants({ variant: 'link' })}>{t('merge.open')}</Link>}
          {p.registerPath && <Link to={p.registerPath} className={buttonVariants({ variant: 'outline' })}><UserPlus aria-hidden />{t('nav.register')}</Link>}
        </>} />

      {/* Search and filter on one 44 px row (audit B-16): the Search button names the field (WCAG G167), the hint sits behind the info button. */}
      <div>
        <form role="search" className="flex flex-wrap items-center gap-2" onSubmit={e => { e.preventDefault(); p.onSubmit(); }}>
          <Label htmlFor="patient-search" className="sr-only">{t('patients.search.label')}</Label>
          <Input id="patient-search" type="search" autoComplete="off" spellCheck={false} maxLength={100} value={p.text} placeholder={t('patients.search.label')}
            onChange={e => p.onTextChange(e.target.value)} aria-describedby={invalid ? `${errorId} ${hintId}` : hintId}
            aria-invalid={invalid || undefined} className="min-w-0 flex-1 basis-60" />
          <Tooltip content={t('patients.search.hint')}>
            <Button type="button" variant="ghost" size="icon" aria-label={t('patients.search.about')}><Info aria-hidden /></Button>
          </Tooltip>
          <Button type="submit"><Search aria-hidden />{t('patients.search.submit')}</Button>
          <span className="flex items-center gap-2">
            <Label htmlFor="patient-filter">{t('patients.filter.label')}</Label>
            <Select value={p.filter} onValueChange={v => p.onFilterChange(v as DirectoryFilter)}>
              <SelectTrigger id="patient-filter" className="w-auto"><SelectValue /></SelectTrigger>
              <SelectContent>{directoryFilters.map(f => <SelectItem key={f} value={f}>{t(`patients.filter.${f}`)}</SelectItem>)}</SelectContent>
            </Select>
          </span>
          <p id={hintId} className="sr-only">{t('patients.search.hint')}</p>
        </form>
        <div aria-live="polite">
          {invalid && (
            <p id={errorId} className="mt-2 flex items-center gap-1.5 text-sm font-semibold text-danger-700">
              <CircleAlert className="size-4 shrink-0" aria-hidden />
              {t(p.errorCode ? `patients.invalid.${p.errorCode}` : 'patients.invalid.generic')}
            </p>
          )}
        </div>
      </div>

      <p role="status" className="sr-only">{status}</p>
      <Results {...p} />
    </PageContainer>
  );
}

function Results(p: Props) {
  const { t } = useI18n();
  const heading = useRef<HTMLHeadingElement>(null);
  // role={undefined}: the page's one status region above announces each change.
  if (p.state === 'loading') return <PageMessage kind="busy" role={undefined}>{t('patients.loading')}</PageMessage>;
  if (p.state === 'forbidden') return <PageMessage kind="denied" title={t('err.403')}>{t('patients.forbidden.body')}</PageMessage>;
  if (p.state === 'error' || p.state === 'notFound')
    return <PageMessage kind="error" role={undefined} title={t('patients.error.title')} action={<Button variant="outline" onClick={p.onRetry}>{t('patients.retry')}</Button>}>{t('patients.error.body')}</PageMessage>;
  if (p.state === 'invalid' || !p.result) return null;

  const r = p.result;
  // The page buttons can disable themselves (first or last page): move focus to the results heading, which stays mounted.
  const toPage = (page: number) => { p.onPageChange(page); heading.current?.focus(); };
  if (r.items.length === 0)
    return (
      <EmptyState action={p.registerPath && <Link to={p.registerPath} className={buttonVariants({ variant: 'outline' })}>{t('patients.empty.register')}</Link>}>
        {t('patients.empty')}
      </EmptyState>
    );

  const pages = pageCount(r);
  return (
    <section aria-labelledby="patient-results" aria-busy={p.updating || undefined} className={cn('flex flex-col gap-3', p.updating && 'opacity-60')}>
      <h2 id="patient-results" ref={heading} tabIndex={-1} className="text-sm font-bold text-muted-foreground focus:outline-none">
        {t(`patients.matchedOn.${r.matchedOn}`, { n: r.total })}
      </h2>
      <Table label={t('patients.tableLabel')} containerClassName="bg-card">
        <TableHeader>
          <TableRow>{directoryColumns.map(c => <TableHead key={c} scope="col">{t(`patients.col.${c}`)}</TableHead>)}</TableRow>
        </TableHeader>
        <TableBody>{r.items.map(row => <Row key={row.id} row={row} to={p.recordPath(row.id)} />)}</TableBody>
      </Table>
      {pages > 1 && (
        <nav aria-label={t('patients.pagination')} className="flex flex-wrap items-center justify-between gap-2">
          <Button variant="outline" disabled={r.page <= 1 || p.updating} onClick={() => toPage(r.page - 1)}>
            <ChevronLeft className="rtl:rotate-180" aria-hidden />{t('patients.page.previous')}
          </Button>
          <p className="text-sm font-semibold">{t('patients.page', { page: r.page, pages })}</p>
          <Button variant="outline" disabled={r.page >= pages || p.updating} onClick={() => toPage(r.page + 1)}>
            {t('patients.page.next')}<ChevronRight className="rtl:rotate-180" aria-hidden />
          </Button>
        </nav>
      )}
    </section>
  );
}

function Row({ row, to }: { row: PatientSummary; to: string }) {
  const { t, formatCalendarDate } = useI18n();
  const age = t(row.ageEstimated ? 'patients.ageEstimated' : 'patients.age', { age: row.ageYears });
  const sex = t(`patients.sex.${row.sexAtBirth}`);
  const incomplete = row.status === 'Incomplete';
  return (
    <TableRow>
      <TableHead scope="row" className="p-0">
        {/* The link's name reads the whole row, so the record opens from the first cell. */}
        <Link to={to} className="flex min-h-12 items-center gap-2 px-3 py-1.5 hover:underline focus-visible:outline-offset-[-2px]">
          <span aria-hidden className="grid size-8 shrink-0 place-items-center rounded-full bg-muted font-extrabold text-brand-700">{row.initials}</span>
          <span aria-hidden className="font-bold">{row.fullName}</span>
          <span className="sr-only">
            {t('patients.row.label', { name: row.fullName, age, sex, mrn: row.mrnTail, phone: row.mobileTail, ...spokenExtras(row, t, formatCalendarDate) })}
            {incomplete && t('patients.row.incomplete')}
          </span>
        </Link>
      </TableHead>
      <TableCell>{age}</TableCell>
      <TableCell>{sex}</TableCell>
      {/* The column heads say MRN and Phone; the cells show only the masked ending (audit B-16). */}
      <TableCell className="font-mono"><bdi dir="ltr">{`··${row.mrnTail}`}</bdi></TableCell>
      <TableCell className="font-mono"><bdi dir="ltr">{`··${row.mobileTail}`}</bdi></TableCell>
      <TableCell className="whitespace-nowrap">{row.lastVisitOn ? formatCalendarDate(row.lastVisitOn) : t('patients.row.noVisit')}</TableCell>
      <TableCell>
        <span className="flex flex-wrap gap-1">
          {row.duplicate && (
            <Tag size="sm" tone={row.duplicate === 'PossibleDuplicate' ? 'warning' : 'neutral'} label={t(`patients.duplicate.${row.duplicate}`)}
              icon={row.duplicate === 'PossibleDuplicate' ? TriangleAlert : undefined} />
          )}
          {incomplete && <Tag size="sm" tone="warning" icon={TriangleAlert} label={t('patients.status.Incomplete')} />}
        </span>
      </TableCell>
    </TableRow>
  );
}
