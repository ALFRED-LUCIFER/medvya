import { Check, Plus } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button } from '@medvya/ui';
import { isOn, testToggles, type Order, type OrderProblem, type OrderSetChoice, type TestToggle } from '../domain/orders';

type Props = {
  orders: Order[]; sets: OrderSetChoice[]; loading: boolean; failed: boolean; ended: boolean; busy: boolean; problem: OrderProblem | null;
  onToggle: (x: TestToggle, on: boolean) => void;
};

/**
 * Tests to order in the plan (MDV-P2-043, design `inv`): a toggle per test of the clinic's order sets and the visit's own. On makes a draft once,
 * off takes the draft out; an ordered test stays ordered and says so. Placing the orders stays the orders panel's own action.
 */
export default function TestToggles(p: Props) {
  const { t } = useI18n();
  if (p.loading) return <p role="status" className="text-sm">{t('orders.loading')}</p>;
  if (p.failed) return <p role="alert" className="text-sm font-bold text-danger-700">{t('orders.loadFailed')}</p>;
  const tests = testToggles(p.sets, p.orders);
  return (
    <div className="flex flex-col gap-2">
      <h4 id="plan-tests" className="font-bold">{t('plan.tests.title')}</h4>
      {tests.length === 0 ? <p className="text-sm text-muted-foreground">{t('plan.tests.empty')}</p> : (
        <ul aria-labelledby="plan-tests" className="flex flex-wrap gap-2">
          {tests.map(x => {
            const on = isOn(x);
            const ordered = x.order?.state === 'Ordered';
            return (
              <li key={x.name}>
                <Button type="button" size="sm" variant={on ? 'selected' : 'outline'} className="min-h-11" aria-pressed={on} disabled={p.busy || p.ended || ordered}
                  onClick={() => p.onToggle(x, !on)}>
                  {on ? <Check aria-hidden className="size-4" /> : <Plus aria-hidden className="size-4" />}
                  {x.name}
                  {ordered && <span className="font-normal">{t('plan.tests.ordered')}</span>}
                </Button>
              </li>
            );
          })}
        </ul>
      )}
      <p className="text-sm text-muted-foreground">{t('plan.tests.hint')}</p>
      {p.problem && <p role="alert" className="text-sm font-bold text-danger-700">{t(`orders.error.${p.problem}`)}</p>}
    </div>
  );
}
