import { Link } from 'react-router';
import { ChevronLeft, ChevronRight, CircleAlert, Search, TriangleAlert } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, Input, Label, buttonVariants, cn } from '@medvya/ui';
import { directoryFilters, pageCount, type DirectoryFilter, type PatientSummary, type SearchErrorCode, type SearchResult } from '../domain/directory';
import PatientsMessage from './PatientsMessage';

type Props = {
  text: string;
  onTextChange: (text: string) => void;
  onSubmit: () => void;
  filter: DirectoryFilter;
  onFilterChange: (filter: DirectoryFilter) => void;
  searched: boolean;
  onPageChange: (page: number) => void;
  result: SearchResult | null;
  state: 'loading' | 'ready' | 'invalid' | 'forbidden' | 'notFound' | 'error';
  errorCode: SearchErrorCode | null;
  onRetry: () => void;
  /** Path of the registration screen, when this membership may register (empty state link). */
  registerPath: string | null;
  recordPath: (id: string) => string;
};

/** Directory and privacy-safe search (MDV-P1-018, D-02 rules): initials, age, sex and MRN tail until a record is opened. */
export default function PatientDirectory(p: Props) {
  const { t } = useI18n();
  const hintId = 'patient-search-hint', errorId = 'patient-search-error';
  const invalid = p.state === 'invalid';
  return (
    <div className="flex max-w-[880px] flex-col gap-5">
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-extrabold">{t('patients.directory.title')}</h1>
        <p className="text-muted-foreground">{t('patients.directory.intro')}</p>
      </div>

      <form role="search" className="flex flex-col gap-2 rounded-xl border bg-card p-4" onSubmit={e => { e.preventDefault(); p.onSubmit(); }}>
        <Label htmlFor="patient-search">{t('patients.search.label')}</Label>
        <div className="flex flex-wrap gap-2">
          <Input id="patient-search" type="search" autoComplete="off" spellCheck={false} maxLength={100} value={p.text}
            onChange={e => p.onTextChange(e.target.value)} aria-describedby={invalid ? `${errorId} ${hintId}` : hintId}
            aria-invalid={invalid || undefined} className="min-h-11 min-w-0 flex-1 basis-60" />
          <Button type="submit"><Search aria-hidden />{t('patients.search.submit')}</Button>
        </div>
        <p id={hintId} className="text-sm text-muted-foreground">{t('patients.search.hint')}</p>
        <div aria-live="polite">
          {invalid && (
            <p id={errorId} className="flex items-center gap-1.5 text-sm font-semibold text-danger-700">
              <CircleAlert className="size-4 shrink-0" aria-hidden />
              {t(p.errorCode ? `patients.invalid.${p.errorCode}` : 'patients.invalid.generic')}
            </p>
          )}
        </div>
      </form>

      <div role="group" aria-label={t('patients.filter.label')} className="flex flex-wrap gap-2">
        {directoryFilters.map(f => (
          <Button key={f} variant={p.filter === f ? 'navy' : 'outline'} aria-pressed={p.filter === f} onClick={() => p.onFilterChange(f)}>
            {t(`patients.filter.${f}`)}
          </Button>
        ))}
      </div>

      <Results {...p} />
    </div>
  );
}

function Results(p: Props) {
  const { t } = useI18n();
  if (p.state === 'loading') return <PatientsMessage busy title={t('patients.loading')} />;
  if (p.state === 'forbidden') return <PatientsMessage title={t('err.403')} body={t('patients.forbidden.body')} />;
  if (p.state === 'error' || p.state === 'notFound')
    return <PatientsMessage alert title={t('patients.error.title')} body={t('patients.error.body')} action={{ label: t('patients.retry'), onClick: p.onRetry }} />;
  if (p.state === 'invalid' || !p.result) return null;

  const r = p.result;
  if (r.items.length === 0)
    return (
      <div role="status" className="flex flex-col items-start gap-2 rounded-xl border bg-card p-6">
        <p className="font-bold">{t('patients.empty')}</p>
        {p.registerPath && <Link to={p.registerPath} className={cn(buttonVariants({ variant: 'link' }), 'px-0')}>{t('patients.empty.register')}</Link>}
      </div>
    );

  const pages = pageCount(r);
  return (
    <section aria-labelledby="patient-results" className="flex flex-col gap-3">
      <div role="status">
        <h2 id="patient-results" className="text-sm font-bold text-muted-foreground">{t(`patients.matchedOn.${r.matchedOn}`, { n: r.total })}</h2>
      </div>
      <ul className="flex flex-col divide-y rounded-xl border bg-card">
        {r.items.map(row => <Row key={row.id} row={row} to={p.recordPath(row.id)} />)}
      </ul>
      {pages > 1 && (
        <nav aria-label={t('patients.pagination')} className="flex flex-wrap items-center justify-between gap-2">
          <Button variant="outline" disabled={r.page <= 1} onClick={() => p.onPageChange(r.page - 1)}>
            <ChevronLeft className="rtl:rotate-180" aria-hidden />{t('patients.page.previous')}
          </Button>
          <p className="text-sm font-semibold">{t('patients.page', { page: r.page, pages })}</p>
          <Button variant="outline" disabled={r.page >= pages} onClick={() => p.onPageChange(r.page + 1)}>
            {t('patients.page.next')}<ChevronRight className="rtl:rotate-180" aria-hidden />
          </Button>
        </nav>
      )}
    </section>
  );
}

function Row({ row, to }: { row: PatientSummary; to: string }) {
  const { t } = useI18n();
  const age = t(row.ageEstimated ? 'patients.ageEstimated' : 'patients.age', { age: row.ageYears });
  const sex = t(`patients.sex.${row.sexAtBirth}`);
  const incomplete = row.status === 'Incomplete';
  return (
    <li>
      <Link to={to} className="flex min-h-14 items-center gap-3 px-4 py-2 hover:bg-accent focus-visible:outline-offset-[-2px]">
        <span className="sr-only">
          {t('patients.row.label', { initials: row.initials.split('').join(' '), age, sex, mrn: row.mrnTail })}
          {incomplete && t('patients.row.incomplete')}
        </span>
        <span aria-hidden className="grid size-9 shrink-0 place-items-center rounded-full bg-muted font-extrabold text-brand-700">{row.initials}</span>
        <span aria-hidden className="flex min-w-0 flex-1 flex-col">
          <span className="font-bold">{t('patients.row.ageSex', { age, sex })}</span>
          <span className="font-mono text-xs text-muted-foreground"><bdi dir="ltr">{t('patients.mrnTail', { mrn: row.mrnTail })}</bdi></span>
        </span>
        {incomplete && (
          <span aria-hidden className="inline-flex shrink-0 items-center gap-1 rounded-sm border border-line-strong bg-card px-2 py-1 text-xs font-bold text-warning-700">
            <TriangleAlert className="size-3.5" aria-hidden />{t('patients.status.Incomplete')}
          </span>
        )}
      </Link>
    </li>
  );
}
