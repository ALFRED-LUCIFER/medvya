import type { FormEventHandler } from 'react';
import type { UseFormReturn } from 'react-hook-form';
import { CircleAlert, CircleCheck, CircleHelp, ShieldCheck, TriangleAlert, type LucideIcon } from 'lucide-react';
import { useI18n, type MessageKey } from '@medvya/i18n';
import { Button, ClinicalAlert, Input } from '@medvya/ui';
import { describedBy, fieldError } from '../../settings/components/fieldStyles';
import { Field } from '../../settings/components/Field';
import { amountOf, kgOf, lookOf, mlOf, type LineSafety, type RxChecks, type SafetyAlert, type WeightDose } from '../domain/checks';

export type SafetyProps = {
  checks: RxChecks | null;
  checksLoading: boolean;
  checksFailed: boolean;
  ended: boolean;
  busy: boolean;
  overriding: string | null;
  reasonForm: UseFormReturn<{ reason: string }>;
  onOverride: (alertKey: string | null) => void;
  onReason: FormEventHandler<HTMLFormElement>;
};

type T = (key: MessageKey, vars?: Record<string, string | number>) => string;

// The rule set's codes (allergy class, effect, status) each have their words in the catalogue.
const words = (t: T, key: string, code: string | null) => (code ? t(`${key}.${code}` as MessageKey) : '');

const amountText = (t: T, mcg: number | null) => (mcg === null ? '' : t(`rx.check.amount.${amountOf(mcg).unit}`, { value: amountOf(mcg).value }));

/** A child's dose by weight, for example "Paracetamol syrup: 15 mg/kg × 14.2 kg = 213 mg = 8.9 mL a dose, at most 4 doses (852 mg) a day." */
const weightText = (t: T, d: WeightDose) => t(d.doseTenthsMl === null ? 'rx.check.weight.dose' : 'rx.check.weight.doseMl', {
  medicine: d.medicine, mgPerKg: d.mgPerKg, kg: kgOf(d.weightHundredthsKg), dose: amountText(t, d.doseMicrograms), ml: mlOf(d.doseTenthsMl ?? 0),
  doses: d.maxDosesPerDay, daily: amountText(t, d.maxDailyMicrograms),
});

/** The alert in words with its evidence, for example "Allergy: Penicillin · verified. Amoxicillin: penicillin class." */
function alertText(t: T, a: SafetyAlert, doses: WeightDose[]): string {
  const base = { medicine: a.medicine ?? '', other: a.other ?? '' };
  const status = words(t, 'rx.check.status', a.allergyStatus);
  const amount = (mcg: number | null) => amountText(t, mcg);
  switch (a.kind) {
    case 'Allergy':
    case 'CrossSensitivity':
      return t(`rx.check.alert.${a.kind}`, { ...base, substance: a.substance ?? '', status, group: words(t, 'rx.check.group', a.code) });
    case 'Interaction':
      return t(a.otherOnChart ? 'rx.check.alert.InteractionChart' : 'rx.check.alert.Interaction', { ...base, effect: words(t, 'rx.check.effect', a.code) });
    case 'Duplicate':
      return t(a.otherOnChart ? 'rx.check.alert.DuplicateChart' : 'rx.check.alert.Duplicate', { ...base, kind: words(t, 'rx.check.duplicate', a.code) });
    case 'DoseAboveMaximum':
      return t('rx.check.alert.DoseAboveMaximum', { ...base, daily: amount(a.dailyMicrograms), max: amount(a.maxDailyMicrograms) });
    case 'AllergiesNotRecorded':
      return t('rx.check.alert.AllergiesNotRecorded');
    case 'Pregnancy':
    case 'Kidney':
    case 'OlderAdult':
      return t(`rx.check.alert.${a.kind}`, { ...base, measure: a.measure ?? '', rule: words(t, 'rx.check.rule', a.code) });
    case 'ChildDose': {
      const d = doses.find(w => w.itemId === a.itemId);
      return t(`rx.check.alert.ChildDose.${a.code === 'doses_a_day' ? 'doses_a_day' : 'per_dose'}`, {
        ...base, mgPerKg: d?.mgPerKg ?? '', kg: d ? kgOf(d.weightHundredthsKg) : '', doses: d?.maxDosesPerDay ?? '',
      });
    }
  }
}

const chipIcon: Record<LineSafety, LucideIcon> = { severe: CircleAlert, moderate: TriangleAlert, overridden: CircleCheck, notChecked: CircleHelp, clear: ShieldCheck };

/** The per-line safety chip: icon and words, never colour alone. */
export function SafetyChip({ state }: { state: LineSafety }) {
  const { t } = useI18n();
  const Icon = chipIcon[state];
  return <span className="inline-flex items-center gap-1 rounded-full border px-2 py-0.5 text-xs font-bold"><Icon aria-hidden className="size-3.5" />{t(`rx.check.chip.${state}`)}</span>;
}

/**
 * The safety checks under the draft (E-11, MDV-P2-016): the allergy state, every alert with its severity in words and its evidence, a reason
 * form for a severe one, what the rules could not check, and the rule set's source and version. The server decides; this only shows it.
 */
export default function SafetyChecks(p: SafetyProps) {
  const { t } = useI18n();
  if (p.checksLoading) return <p role="status">{t('rx.check.loading')}</p>;
  if (p.checksFailed) return <p role="alert" className={fieldError}>{t('rx.check.failed')}</p>;
  const c = p.checks;
  if (!c) return null;
  const error = p.reasonForm.formState.errors.reason;

  return (
    <section aria-labelledby="rx-checks-title" className="flex flex-col gap-3 border-t pt-4">
      <h3 id="rx-checks-title" className="flex items-center gap-2 font-bold"><ShieldCheck aria-hidden className="size-4" />{t('rx.check.title')}</h3>
      <p className="text-sm">{t(`rx.check.allergies.${c.allergies}`)}</p>
      {c.unresolved > 0 && <p className="text-sm font-bold">{t('rx.check.unresolved', { count: c.unresolved })}</p>}
      {c.alerts.length === 0 ? <p className="text-sm">{t('rx.check.clear')}</p> : (
        <ul aria-label={t('rx.check.alerts')} className="flex flex-col gap-3">
          {c.alerts.map(a => {
            const given = c.overrides.find(o => o.key === a.key);
            return (
              <li key={a.key} className="flex flex-col gap-2">
                <ClinicalAlert severity={lookOf(a, !!given)}>{`${t(`rx.check.severity.${a.severity}`)} · ${alertText(t, a, c.weightDoses)}`}</ClinicalAlert>
                {given && <p className="text-sm">{t('rx.check.overridden', { reason: given.reason })}</p>}
                {!given && a.severity === 'Severe' && !p.ended && (p.overriding === a.key ? (
                  <form noValidate onSubmit={p.onReason} className="flex flex-col gap-2">
                    <Field id="rx-override-reason" label={t('rx.check.reason')} hint={t('rx.check.reasonHint')} error={error && t('rx.check.reasonRequired')}>
                      <Input id="rx-override-reason" className="h-11" autoComplete="off" {...describedBy('rx-override-reason', !!error, true)} {...p.reasonForm.register('reason')} />
                    </Field>
                    <div className="flex flex-wrap gap-2">
                      <Button type="submit" className="min-h-11" disabled={p.busy}>{t('rx.check.record')}</Button>
                      <Button type="button" variant="outline" className="min-h-11" onClick={() => p.onOverride(null)}>{t('rx.check.cancel')}</Button>
                    </div>
                  </form>
                ) : (
                  <Button type="button" variant="outline" className="min-h-11 self-start" disabled={p.busy} onClick={() => p.onOverride(a.key)}>{t('rx.check.override')}</Button>
                ))}
              </li>
            );
          })}
        </ul>
      )}
      {c.weightDoses.length > 0 && (
        <div className="flex flex-col gap-1">
          <h4 className="text-sm font-bold">{t('rx.check.weight.title')}</h4>
          <ul className="flex list-disc flex-col gap-1 ps-5 text-sm">
            {c.weightDoses.map(d => <li key={d.itemId}>{weightText(t, d)}</li>)}
          </ul>
        </div>
      )}
      {c.notChecked.length > 0 && (
        <div className="flex flex-col gap-1">
          <h4 className="text-sm font-bold">{t('rx.check.notChecked')}</h4>
          <ul className="flex list-disc flex-col gap-1 ps-5 text-sm">
            {c.notChecked.map(n => <li key={`${n.id}-${n.reason}`}>{t(`rx.check.missed.${n.reason}`, { medicine: n.medicine })}</li>)}
          </ul>
        </div>
      )}
      <details className="rounded-md border p-3 text-sm">
        <summary className="flex min-h-11 cursor-pointer items-center font-bold">{t('rx.check.info.title')}</summary>
        <p>{t('rx.check.info.source', { source: words(t, 'rx.check.source', c.ruleSet) })}</p>
        {/* The version is a Latin code: isolated so right-to-left text does not reorder it. */}
        <p>{t('rx.check.info.version')} <bdi dir="ltr">{c.ruleSetVersion}</bdi></p>
        <p className="mt-2">{t('rx.check.info.limits')}</p>
      </details>
    </section>
  );
}
