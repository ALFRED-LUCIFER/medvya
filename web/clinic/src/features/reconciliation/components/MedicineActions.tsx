import { Link } from 'react-router';
import { ltr, useI18n } from '@medvya/i18n';
import { buttonVariants } from '@medvya/ui';
import type { Problem, Reconciliation } from '../domain/reconciliation';
import { DecisionCell, type DecisionProps } from './ReconciliationPage';

type Props = DecisionProps & {
  view: Reconciliation | null; loading: boolean; failed: boolean; problem: Problem | null; reconcilePath: string;
};

/**
 * The plan's medicines (MDV-P2-043, design `rx`): each medicine of the chart with its action (keep, change to a new dose, taper in steps to an end
 * date, or stop), decided in the medication reconciliation (the same rows and form as its page). New medicines are the prescription's own lines.
 */
export default function MedicineActions(p: Props) {
  const { t } = useI18n();
  if (p.loading) return <p role="status" className="text-sm">{t('reconcile.loading')}</p>;
  if (p.failed || !p.view) return <p role="alert" className="text-sm font-bold text-danger-700">{t('reconcile.loadFailed')}</p>;
  const locked = p.view.confirmedAt !== null;
  return (
    <div className="flex flex-col gap-2">
      <h4 id="plan-medicines" className="font-bold">{t('plan.medicines.title')}</h4>
      {p.view.rows.length === 0 ? <p className="text-sm text-muted-foreground">{t('reconcile.empty')}</p> : (
        <ul aria-labelledby="plan-medicines" className="flex flex-col divide-y rounded-lg border">
          {p.view.rows.map(r => (
            <li key={r.key} className="flex flex-col gap-2 p-3">
              <p><span className="font-bold">{r.name}</span>{r.chart?.dose && <span className="ms-2 text-sm text-muted-foreground">{ltr(r.chart.dose)}</span>}</p>
              <DecisionCell row={r} {...p} locked={locked} />
            </li>
          ))}
        </ul>
      )}
      {p.problem && <p role="alert" className="text-sm font-bold text-danger-700">{t(`reconcile.error.${p.problem}`)}</p>}
      <Link to={p.reconcilePath} className={buttonVariants({ variant: 'outline', className: 'min-h-11 self-start' })}>{t('plan.medicines.reconcile')}</Link>
    </div>
  );
}
