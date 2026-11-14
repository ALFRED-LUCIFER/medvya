import { Link } from 'react-router';
import { Search } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import {
  Button, buttonVariants, Dialog, DialogContent, DialogDescription, DialogTitle, DialogTrigger, Input, Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from '@medvya/ui';
import { accessReasons, type AccessReason } from '../../clinical-access/domain/clinicalAccess';
import PatientSummaryLine from '../../patients/components/PatientSummaryLine';
import type { LookupRow } from '../domain/lookup';

type Props = {
  open: boolean; onOpenChange: (open: boolean) => void;
  text: string; onText: (t: string) => void;
  rows: LookupRow[]; active: number; onActive: (i: number) => void; current: LookupRow | null; total: number;
  status: 'idle' | 'searching' | 'found' | 'none' | 'failed';
  reason: AccessReason; onReason: (r: AccessReason) => void; asking: boolean; askFailed: boolean;
  onRequestAccess: (row: LookupRow) => void; onOpenChart: (row: LookupRow) => void; onClose: () => void;
  onKeyDown: (e: React.KeyboardEvent) => void;
  /** Where "Start visit" (a walk-in for this patient) and "Book" go, when this role may; "Register" when it may register. */
  startVisitPath: string | null; bookPath: string | null; registerPath: string | null;
};

/** D-02 global search overlay: opened with ⌘K / Ctrl+K or the header button. Each row is the desk's search row (MDV-P1-101); numbers stay masked until a chart is opened. */
export default function GlobalSearch(p: Props) {
  const { t } = useI18n();
  return (
    <Dialog open={p.open} onOpenChange={p.onOpenChange}>
      <DialogTrigger asChild>
        <Button variant="outline" className="max-sm:size-11 max-sm:px-0" aria-keyshortcuts="Control+K Meta+K">
          <Search aria-hidden /><span className="max-sm:sr-only">{t('patients.search.open')}</span>
          {/* Key names read left to right in every language; without it Arabic reorders "⌘K / Ctrl K". */}
          <kbd dir="ltr" className="hidden rounded border px-1.5 text-xs font-semibold text-muted-foreground lg:inline" aria-hidden>{t('globalSearch.shortcut')}</kbd>
        </Button>
      </DialogTrigger>
      <DialogContent closeLabel={t('globalSearch.close')} aria-describedby="gs-description">
        <DialogTitle>{t('patients.search.open')}</DialogTitle>
        <DialogDescription id="gs-description">{t('globalSearch.description')}</DialogDescription>
        <Input role="combobox" aria-expanded={p.rows.length > 0} aria-controls="gs-list" aria-autocomplete="list" autoComplete="off" autoFocus
          aria-activedescendant={p.active >= 0 ? `gs-row-${p.active}` : undefined} aria-label={t('globalSearch.input')} placeholder={t('globalSearch.input')} value={p.text} onChange={e => p.onText(e.target.value)} onKeyDown={p.onKeyDown} />
        <p role="status" className="text-sm text-muted-foreground">
          {p.status === 'found' ? t('globalSearch.status.found', { count: p.total }) : t(`globalSearch.status.${p.status}`)}
        </p>
        {p.rows.length > 0 && (
          <ul id="gs-list" role="listbox" aria-label={t('globalSearch.list')} className="flex flex-col gap-1">
            {p.rows.map((r, i) => <Row key={r.id} row={r} index={i} selected={i === p.active} onActive={p.onActive} />)}
          </ul>
        )}
        {p.status === 'none' && p.registerPath && (
          <Link to={p.registerPath} onClick={p.onClose} className={buttonVariants({ variant: 'outline', className: 'w-fit' })}>{t('globalSearch.register')}</Link>
        )}
        {p.current && <Actions {...p} row={p.current} />}
      </DialogContent>
    </Dialog>
  );
}

function Row({ row, index, selected, onActive }: { row: LookupRow; index: number; selected: boolean; onActive: (i: number) => void }) {
  const { t } = useI18n();
  const note = row.access === 'CanRequest' ? t('globalSearch.notYours') : row.access === 'RequestPending' ? t('globalSearch.pending') : row.access === 'NoAccess' ? t('globalSearch.noChart') : null;
  return (
    // The row is text only; its actions sit below the list, so nothing interactive is nested in the option.
    <li id={`gs-row-${index}`} role="option" aria-selected={selected} onClick={() => onActive(index)}
      className="flex min-h-12 cursor-pointer flex-wrap items-center gap-x-3 gap-y-0.5 rounded-md border px-3 py-2 text-sm aria-selected:border-action-600 aria-selected:bg-muted">
      <PatientSummaryLine row={row} spoken />
      {note && <span className="text-xs font-bold text-ink-700">{note}</span>}
    </li>
  );
}

function Actions(p: Props & { row: LookupRow }) {
  const { t } = useI18n();
  const { row } = p;
  return (
    <div role="group" aria-label={t('globalSearch.actions')} className="flex flex-col gap-2">
      {row.access === 'Open' && (
        <div className="flex flex-wrap gap-2">
          <Button variant="default" onClick={() => p.onOpenChart(row)}>{t('globalSearch.openChart')}</Button>
          {p.startVisitPath && <Link to={`${p.startVisitPath}?patient=${row.id}`} onClick={p.onClose} className={buttonVariants({ variant: 'outline' })}>{t('globalSearch.startVisit')}</Link>}
          {p.bookPath && <Link to={`${p.bookPath}?patient=${row.id}`} onClick={p.onClose} className={buttonVariants({ variant: 'outline' })}>{t('globalSearch.book')}</Link>}
        </div>
      )}
      {row.access === 'CanRequest' && (
        <div role="alert" className="flex flex-col gap-2 rounded-md border p-2 text-sm">
          <p className="font-bold">{t('consult.refused.ask')}</p>
          <Select value={p.reason} onValueChange={v => p.onReason(v as AccessReason)}>
            <SelectTrigger className="w-full" aria-label={t('consult.refused.reason')}><SelectValue /></SelectTrigger>
            <SelectContent>{accessReasons.map(r => <SelectItem key={r} value={r}>{t(`clinicalAccess.reason.${r}`)}</SelectItem>)}</SelectContent>
          </Select>
          <Button className="whitespace-normal" loading={p.asking} onClick={() => p.onRequestAccess(row)}>{t('state.noAccess.cta')}</Button>
          {p.askFailed && <p className="font-bold text-danger-700">{t('consult.failed')}</p>}
        </div>
      )}
      {row.access === 'RequestPending' && <p role="status" className="rounded-md border p-2 text-sm font-bold">{t('consult.refused.pending')}</p>}
      {row.access === 'NoAccess' && <p className="rounded-md border p-2 text-sm font-bold">{t('globalSearch.noAccess')}</p>}
    </div>
  );
}
