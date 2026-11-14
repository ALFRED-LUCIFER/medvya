import { ChevronDown, Download, X } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, Field, Input, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Table, TableBody, TableCell, TableHead, TableHeader, TablePagination, TableRow, TableState } from '@medvya/ui';
import { Section } from '../../settings/components/Section';
import { fieldError } from '../../settings/components/fieldStyles';
import { PatientPicker, type PatientSearchView } from '../../schedule/components/BookingPanel';
import type { TableView } from '../../table/domain/tableSort';
import { actionKey, all, auditColumns, chart, patientRecords, type AuditFilters, type AuditRow, type AuditSortColumn } from '../domain/auditLog';

type Props = {
  filters: AuditFilters;
  filtersOpen: boolean;
  people: { id: string; name: string }[];
  actions: string[];
  /** The sorted page shown. */
  table: TableView<AuditRow, AuditSortColumn>;
  patientName: string | null;
  patientSearch: PatientSearchView;
  state: 'loading' | 'ready' | 'forbidden' | 'error';
  canLoadMore: boolean;
  loadingMore: boolean;
  exporting: boolean;
  exportFailed: boolean;
  onFilters: (patch: Partial<AuditFilters>) => void;
  onToggleFilters: () => void;
  onClearPatient: () => void;
  onMore: () => void;
  onExport: () => void;
};

/** Practice access log (F-12): who did what to which record, when, why, and whether it was allowed. Outcome is always written out, never colour alone. */
export default function AuditLogPanel(p: Props) {
  const { t, formatDate, formatTime, formatCalendarDate } = useI18n();
  const actionName = (code: string) => t(actionKey(code) ?? 'audit.actionUnknown');
  if (p.state === 'forbidden') return <p role="alert" className={fieldError}>{t('audit.forbidden')}</p>;
  const zone = p.filters.zone;
  const nowrap = 'md:whitespace-nowrap';
  const f = p.filters;
  // The filters fold away, so one line always says what the table holds (MDV-P2-054 AC3).
  const view = f.action === chart ? t('audit.chartView') : f.action === patientRecords ? t('audit.patientRecords') : f.action === all ? t('audit.anyAction') : actionName(f.action);
  const who = f.actor === all ? t('audit.everyone') : (p.people.find(s => s.id === f.actor)?.name ?? t('audit.unknownStaff'));
  const day = (d: string) => (d ? formatCalendarDate(d) : t('audit.anyDate'));
  const summary = t('audit.showing', { filters: [view, who, t('audit.dateRange', { from: day(f.from), to: day(f.to) })].join(' · ') });
  return (
    <Section title={t('audit.title')} intro={summary} actions={<>
      <Button type="button" variant="outline" aria-expanded={p.filtersOpen} aria-controls="audit-filters" onClick={p.onToggleFilters}>
        {t('audit.filters')}<ChevronDown aria-hidden className={p.filtersOpen ? 'size-4 rotate-180' : 'size-4'} />
      </Button>
      <Button type="button" variant="outline" loading={p.exporting} onClick={p.onExport}><Download aria-hidden className="size-4" />{t('audit.export')}</Button>
    </>}>
      {p.exportFailed && <p role="alert" className={fieldError}>{t('audit.exportFailed')}</p>}
      <div id="audit-filters" hidden={!p.filtersOpen} className="flex flex-col gap-4">
        <p className="text-muted-foreground">{t('audit.intro')}</p>
        <div className="grid gap-4 md:grid-cols-2">
          <Select value={f.actor} onValueChange={v => p.onFilters({ actor: v })}>
            <Field id="audit-actor" label={t('audit.staff')}>
              <SelectTrigger className="w-full"><SelectValue /></SelectTrigger>
            </Field>
            <SelectContent>
              <SelectItem value={all}>{t('audit.everyone')}</SelectItem>
              {p.people.map(s => <SelectItem key={s.id} value={s.id}>{s.name}</SelectItem>)}
            </SelectContent>
          </Select>
          <Select value={f.action} onValueChange={v => p.onFilters({ action: v })}>
            <Field id="audit-action" label={t('audit.action')}>
              <SelectTrigger className="w-full"><SelectValue /></SelectTrigger>
            </Field>
            <SelectContent>
              <SelectItem value={chart}>{t('audit.chartView')}</SelectItem>
              <SelectItem value={patientRecords}>{t('audit.patientRecords')}</SelectItem>
              <SelectItem value={all}>{t('audit.anyAction')}</SelectItem>
              {p.actions.map(a => <SelectItem key={a} value={a}>{actionName(a)}</SelectItem>)}
            </SelectContent>
          </Select>
          <Field id="audit-from" label={t('audit.from')}>
            <Input type="date" value={f.from} max={f.to || undefined} onChange={e => p.onFilters({ from: e.target.value })} />
          </Field>
          <Field id="audit-to" label={t('audit.to')}>
            <Input type="date" value={f.to} min={f.from || undefined} onChange={e => p.onFilters({ to: e.target.value })} />
          </Field>
        </div>
        {!f.patientId && <PatientPicker search={p.patientSearch} chosen="" registerPath={null} invalid={false} />}
      </div>

      {f.patientId && (
        <div className="flex flex-wrap items-center gap-2 rounded-lg border p-3">
          <span className="font-bold">{p.patientName ? t('audit.patientChosen', { name: p.patientName }) : t('audit.patientChosenNoName')}</span>
          <Button type="button" variant="outline" onClick={p.onClearPatient}><X aria-hidden className="size-4" />{t('audit.clearPatient')}</Button>
        </div>
      )}

      <TableState state={p.state === 'loading' || p.state === 'error' ? p.state : p.table.rows.length === 0 ? 'empty' : 'ready'} messages={{ loading: t('settings.loading'), error: t('audit.loadFailed'), empty: t('audit.empty') }}>
        {/* Compact: a reference list read with a mouse (DESIGN.md, Density); stacked under 768 px with the time first. The short columns
            keep to one line, so a row is 36 px unless a name or an action is long. */}
        <Table label={t('audit.tableLabel')} density="compact" stacked status={p.table.status}>
          <TableHeader>
            <TableRow>
              {auditColumns.map(c => (
                <TableHead key={c.key} scope="col" sort={c.sortable ? p.table.sortOf(c.key) : undefined} onSort={c.sortable ? () => p.table.onSort(c.key) : undefined}>{t(`audit.col.${c.key}`)}</TableHead>
              ))}
            </TableRow>
          </TableHeader>
          <TableBody>
            {p.table.rows.map(r => {
              const at = new Date(r.occurredAt);
              return (
                <TableRow key={r.id}>
                  <TableHead scope="row"><time dateTime={r.occurredAt}>{formatDate(at, zone)} {formatTime(at, zone)}</time></TableHead>
                  <TableCell label={t('audit.col.staff')}>{r.actorName ?? t('audit.unknownStaff')}</TableCell>
                  <TableCell label={t('audit.col.role')} className={nowrap}>{r.role ? t(`role.${r.role}`) : t('audit.roleNotRecorded')}</TableCell>
                  <TableCell label={t('audit.col.location')} className={nowrap}>{r.location ?? t('audit.roleNotRecorded')}</TableCell>
                  <TableCell label={t('audit.col.action')}>{actionName(r.action)}{r.detail && <span className="block text-xs text-muted-foreground">{r.detail}</span>}</TableCell>
                  <TableCell label={t('audit.col.patient')} className={nowrap}>{r.patientName ?? (r.patientMrn ? t('audit.mrn', { mrn: r.patientMrn }) : '')}</TableCell>
                  <TableCell label={t('audit.col.purpose')} className={nowrap}>{r.purpose}</TableCell>
                  <TableCell label={t('audit.col.outcome')} className={nowrap}>{t(`audit.outcome.${r.outcome}`)}</TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
        <TablePagination page={p.table.page} pageCount={p.table.pageCount} onPage={p.table.onPage} />
      </TableState>
      {p.canLoadMore && <Button type="button" variant="outline" className="self-start" loading={p.loadingMore} onClick={p.onMore}>{t('audit.more')}</Button>}
      <p className="text-sm text-muted-foreground">{t('audit.exportNote')} {t('audit.exportAll')}</p>
    </Section>
  );
}
