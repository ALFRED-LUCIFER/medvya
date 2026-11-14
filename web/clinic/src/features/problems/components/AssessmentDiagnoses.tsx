import { useId, useRef, useState, type KeyboardEvent } from 'react';
import { Link } from 'react-router';
import { CircleHelp, Plus } from 'lucide-react';
import { ltr, useI18n } from '@medvya/i18n';
import { Button, buttonVariants, Chip } from '@medvya/ui';
import type { Problem, ProblemList, ProblemRow } from '../domain/problems';
import ProblemAddForm, { type ProblemAddFormProps } from './ProblemAddForm';

export type AssessmentDiagnosesProps = ProblemAddFormProps & {
  view: ProblemList | null; loading: boolean; problem: Problem | null;
  /** The full problems page (E-07), still reachable for editing and resolving. */
  problemsPath: string;
};

/**
 * This consultation's active diagnoses under Assessment (MDV-P1-094, E-05), added inline with the problems page's own form and controller, so
 * the route and the typed note never change. Each chip says its state in words (existing or new, and provisional or patient reported).
 */
export default function AssessmentDiagnoses(p: AssessmentDiagnosesProps) {
  const { t } = useI18n();
  const [open, setOpen] = useState(false);
  const toggle = useRef<HTMLButtonElement>(null);
  const areaId = useId();
  if (p.loading) return <p role="status" className="text-sm">{t('problems.loading')}</p>;
  if (!p.view) return <p role="alert" className="text-sm font-bold">{t('problems.loadFailed')}</p>;
  const rows = p.view.rows.filter(r => r.status === 'Active');
  const close = () => { setOpen(false); toggle.current?.focus(); };
  // Escape from a Select list (portalled, outside this area) only closes that list.
  const onKeyDown = (e: KeyboardEvent<HTMLDivElement>) => {
    if (e.key === 'Escape' && e.currentTarget.contains(e.target as Node)) { e.preventDefault(); close(); }
  };
  return (
    <div className="flex flex-col gap-2">
      <div className="flex flex-wrap items-center gap-2">
        {rows.length === 0
          ? <p className="text-sm text-muted-foreground">{t('problems.inline.empty')}</p>
          : <ul aria-label={t('problems.inline.label')} className="flex flex-wrap gap-2">
              {rows.map(r => <li key={r.id}><DiagnosisChip row={r} /></li>)}
            </ul>}
        {!p.view.locked && (
          <Button ref={toggle} type="button" variant="ghost" aria-expanded={open} aria-controls={areaId} onClick={() => setOpen(o => !o)}>
            <Plus aria-hidden />{t('problems.inline.add')}
          </Button>
        )}
      </div>
      {open && !p.view.locked && (
        <div id={areaId} onKeyDown={onKeyDown} className="flex flex-col gap-2">
          <ProblemAddForm {...p} autoFocus />
          <div><Button type="button" variant="outline" onClick={close}>{t('problems.inline.close')}</Button></div>
        </div>
      )}
      {p.problem && <p role="alert" className="font-bold text-danger-700">{t(`problems.error.${p.problem}`)}</p>}
      <div><Link to={p.problemsPath} className={buttonVariants({ variant: 'link' })}>{t('problems.inline.open')}</Link></div>
    </div>
  );
}

/** Code (kept left to right), description and state in words; provisional also gets an icon and a dashed outline, as on the problems page. */
function DiagnosisChip({ row }: { row: ProblemRow }) {
  const { t } = useI18n();
  const provisional = row.verification === 'Provisional';
  const state = [t(row.addedHere ? 'problems.inline.new' : 'problems.inline.existing'), row.verification !== 'Verified' && t(`problems.verification.${row.verification}`)];
  const label = [row.code ? `${ltr(row.code)} ${row.description}` : row.description, ...state].filter(Boolean).join(' · ');
  return <Chip tone="info" label={label} icon={provisional ? CircleHelp : undefined} className={`whitespace-normal text-start ${provisional ? 'border-dashed border-foreground' : ''}`} />;
}
