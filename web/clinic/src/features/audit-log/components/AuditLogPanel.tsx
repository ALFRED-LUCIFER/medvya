import { Download, X } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, Input, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Table, TableBody, TableCell, TableHead, TableHeader, TableRow, TableState } from '@medvya/ui';
import { Field } from '../../settings/components/Field';
import { Section } from '../../settings/components/Section';
import { fieldError } from '../../settings/components/fieldStyles';
import { PatientPicker, type PatientSearchView } from '../../schedule/components/BookingPanel';
import { actionKey, all, type AuditFilters, type AuditRow } from '../domain/auditLog';

type Props = {
  filters: AuditFilters;
  people: { id: string; name: string }[];
  actions: string[];
  rows: AuditRow[];
  patientName: string | null;
  patientSearch: PatientSearchView;
  state: 'loading' | 'ready' | 'forbidden' | 'error';
  canLoadMore: boolean;
  loadingMore: boolean;
  exporting: boolean;
  exportFailed: boolean;
  onFilters: (patch: Partial<AuditFilters>) => void;
  onClearPatient: () => void;
  onMore: () => void;
  onExport: () => void;
};

/** Practice access log (F-12): who did what to which record, when, why, and whether it was allowed. Outcome is always written out, never colour alone. */
export default function AuditLogPanel(p: Props) {
  const { t, formatDate, formatTime } = useI18n();
  const actionName = (code: string) => { const key = actionKey(code); return key ? t(key) : code; };
  if (p.state === 'forbidden') return <p role="alert" className={fieldError}>{t('audit.forbidden')}</p>;
  const zone = p.filters.zone;
  return (
    <Section title={t('audit.title')} intro={t('audit.intro')}>
      <div className="grid gap-4 sm:grid-cols-2">
        <Field id="audit-actor" label={t('audit.staff')}>
          <Select value={p.filters.actor} onValueChange={v => p.onFilters({ actor: v })}>
            <SelectTrigger id="audit-actor" className="h-11 w-full"><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value={all}>{t('audit.everyone')}</SelectItem>
              {p.people.map(s => <SelectItem key={s.id} value={s.id}>{s.name}</SelectItem>)}
            </SelectContent>
          </Select>
        </Field>
        <Field id="audit-action" label={t('audit.action')}>
          <Select value={p.filters.action} onValueChange={v => p.onFilters({ action: v })}>
            <SelectTrigger id="audit-action" className="h-11 w-full"><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value={all}>{t('audit.anyAction')}</SelectItem>
              {p.actions.map(a => <SelectItem key={a} value={a}>{actionName(a)}</SelectItem>)}
            </SelectContent>
          </Select>
        </Field>
        <Field id="audit-from" label={t('audit.from')}>
          <Input id="audit-from" type="date" className="h-11" value={p.filters.from} max={p.filters.to || undefined} onChange={e => p.onFilters({ from: e.target.value })} />
        </Field>
        <Field id="audit-to" label={t('audit.to')}>
          <Input id="audit-to" type="date" className="h-11" value={p.filters.to} min={p.filters.from || undefined} onChange={e => p.onFilters({ to: e.target.value })} />
        </Field>
      </div>

      {p.filters.patientId ? (
        <div className="flex flex-wrap items-center gap-2 rounded-lg border p-3">
          <span className="font-bold">{p.patientName ? t('audit.patientChosen', { name: p.patientName }) : t('audit.patientChosenNoName')}</span>
          <Button type="button" variant="outline" className="min-h-11" onClick={p.onClearPatient}><X aria-hidden className="size-4" />{t('audit.clearPatient')}</Button>
        </div>
      ) : (
        <PatientPicker search={p.patientSearch} chosen="" registerPath={null} invalid={false} />
      )}

      <div className="flex flex-wrap items-center gap-3">
        <Button type="button" variant="outline" loading={p.exporting} onClick={p.onExport}><Download aria-hidden className="size-4" />{t('audit.export')}</Button>
        <span className="text-sm text-muted-foreground">{t('audit.exportNote')}</span>
        {p.exportFailed && <p role="alert" className={fieldError}>{t('audit.exportFailed')}</p>}
      </div>

      <TableState state={p.state === 'loading' || p.state === 'error' ? p.state : p.rows.length === 0 ? 'empty' : 'ready'} messages={{ loading: t('settings.loading'), error: t('audit.loadFailed'), empty: t('audit.empty') }}>
        <Table label={t('audit.tableLabel')} className="min-w-[800px]">
          <TableHeader>
            <TableRow>{(['time', 'staff', 'role', 'location', 'action', 'patient', 'purpose', 'outcome'] as const).map(c => <TableHead key={c} scope="col">{t(`audit.col.${c}`)}</TableHead>)}</TableRow>
          </TableHeader>
          <TableBody>
            {p.rows.map(r => {
              const at = new Date(r.occurredAt);
              return (
                <TableRow key={r.id}>
                  <TableCell className="whitespace-nowrap"><time dateTime={r.occurredAt}>{formatDate(at, zone)} {formatTime(at, zone)}</time></TableCell>
                  <TableCell>{r.actorName ?? t('audit.unknownStaff')}</TableCell>
                  <TableCell>{r.role ? t(`role.${r.role}`) : t('audit.roleNotRecorded')}</TableCell>
                  <TableCell>{r.location ?? t('audit.roleNotRecorded')}</TableCell>
                  <TableCell>{actionName(r.action)}{r.detail && <span className="block text-xs text-muted-foreground">{r.detail}</span>}</TableCell>
                  <TableCell>{r.patientName ?? (r.patientMrn ? t('audit.mrn', { mrn: r.patientMrn }) : '')}</TableCell>
                  <TableCell>{r.purpose}</TableCell>
                  <TableCell className="font-semibold">{t(`audit.outcome.${r.outcome}`)}</TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      </TableState>
      {p.canLoadMore && <Button type="button" variant="outline" className="self-start" loading={p.loadingMore} onClick={p.onMore}>{t('audit.more')}</Button>}
    </Section>
  );
}
