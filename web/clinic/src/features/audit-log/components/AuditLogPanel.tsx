import { Download, X } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, Input, Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@medvya/ui';
import { Field } from '../../settings/components/Field';
import { Section } from '../../settings/components/Section';
import { fieldError } from '../../settings/components/fieldStyles';
import { PatientPicker, type PatientSearchView } from '../../schedule/components/BookingPanel';
import { all, type AuditFilters, type AuditRow } from '../domain/auditLog';

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
              {p.actions.map(a => <SelectItem key={a} value={a}>{a}</SelectItem>)}
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

      <div aria-live="polite">
        {p.state === 'loading' && <p role="status">{t('settings.loading')}</p>}
        {p.state === 'error' && <p role="alert" className={fieldError}>{t('audit.loadFailed')}</p>}
        {p.state === 'ready' && p.rows.length === 0 && <p className="font-bold">{t('audit.empty')}</p>}
      </div>

      {p.state === 'ready' && p.rows.length > 0 && (
        <div className="overflow-x-auto rounded-lg border" tabIndex={0} role="region" aria-label={t('audit.tableLabel')}>
          <table className="w-full min-w-[800px] text-start text-sm">
            <thead className="bg-muted text-start">
              <tr>{(['time', 'staff', 'role', 'action', 'patient', 'purpose', 'outcome'] as const).map(c => <th key={c} scope="col" className="p-3 text-start font-bold">{t(`audit.col.${c}`)}</th>)}</tr>
            </thead>
            <tbody className="divide-y">
              {p.rows.map(r => {
                const at = new Date(r.occurredAt);
                return (
                  <tr key={r.id}>
                    <td className="p-3 whitespace-nowrap"><time dateTime={r.occurredAt}>{formatDate(at)} {formatTime(at, zone)}</time></td>
                    <td className="p-3">{r.actorName ?? t('audit.unknownStaff')}</td>
                    <td className="p-3">{r.role ? t(`role.${r.role}`) : t('audit.roleNotRecorded')}</td>
                    <td className="p-3">{r.action}{r.detail && <span className="block text-xs text-muted-foreground">{r.detail}</span>}</td>
                    <td className="p-3">{r.patientName ?? (r.patientMrn ? t('audit.mrn', { mrn: r.patientMrn }) : '')}</td>
                    <td className="p-3">{r.purpose}</td>
                    <td className="p-3 font-semibold">{t(`audit.outcome.${r.outcome}`)}</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
      {p.canLoadMore && <Button type="button" variant="outline" className="self-start" loading={p.loadingMore} onClick={p.onMore}>{t('audit.more')}</Button>}
    </Section>
  );
}
