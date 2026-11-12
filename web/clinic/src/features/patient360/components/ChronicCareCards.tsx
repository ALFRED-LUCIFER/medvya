import type { FormEventHandler, ReactNode } from 'react';
import type { UseFormReturn } from 'react-hook-form';
import { ltr, useI18n } from '@medvya/i18n';
import { Button, Input } from '@medvya/ui';
import { CircleAlert, CircleCheck, CircleMinus } from 'lucide-react';
import { Field } from '../../settings/components/Field';
import { describedBy, fieldError } from '../../settings/components/fieldStyles';
import {
  checkKindOf, gapKey, type CareError, type CareGap, type CareTarget, type CheckForm, type ChronicCare, type GapOutcome, type GoalForm, type ProgressForm,
  type TargetCode, type TargetForm,
} from '../domain/chronicCare';

type Submit = FormEventHandler<HTMLFormElement>;
type Props = {
  care: ChronicCare | null; loading: boolean; failed: boolean; hidden: boolean;
  editing: TargetCode | null; targetForm: UseFormReturn<TargetForm>; savingTarget: boolean; targetFailed: boolean;
  onEditTarget: (code: TargetCode, limit: number) => void; onCancelTarget: () => void; onSubmitTarget: Submit;
  outcome: { key: string; outcome: GapOutcome } | null; acting: CareGap | null | undefined; onAct: (g: CareGap) => void;
  checking: string | null; checkForm: UseFormReturn<CheckForm>; savingCheck: boolean; checkFailed: boolean;
  onCheck: (g: CareGap) => void; onCancelCheck: () => void; onSubmitCheck: (g: CareGap) => Submit;
  adding: boolean; goalForm: UseFormReturn<GoalForm>; savingGoal: boolean; goalFailed: boolean; onAddGoal: () => void; onCancelGoal: () => void; onSubmitGoal: Submit;
  moving: string | null; progressForm: UseFormReturn<ProgressForm>; savingProgress: boolean; progressFailed: boolean;
  onMove: (id: string, current: number) => void; onCancelMove: () => void; onSubmitMove: Submit;
  /** A doctor (Patient 360 at Full, prescriptions and orders at Sign): sets targets and goals, records checks and acts on gaps. The server still decides. */
  canDecide: boolean;
};

/**
 * The chart's right column for chronic care (MDV-P2-038, E-01, design `targets`, `gaps`, `goals`): each latest value against its target,
 * the care gaps due today with their action, and the care-plan goals. Met and due are said in words beside an icon, never by colour alone.
 */
export default function ChronicCareCards(p: Props) {
  const { t } = useI18n();
  if (p.hidden) return null;
  if (p.loading) return <Card title={t('chronicCare.gaps.title')}><p role="status">{t('patient360.loading')}</p></Card>;
  if (p.failed || !p.care) return <Card title={t('chronicCare.gaps.title')}><p role="alert" className={fieldError}>{t('patient360.loadFailed')}</p></Card>;
  return (
    <>
      {p.care.chronic && <Targets {...p} />}
      <Gaps {...p} />
      <Goals {...p} />
    </>
  );
}

function Card({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section aria-label={title} className="flex flex-col gap-3 rounded-xl border bg-card p-4">
      <h2 className="text-base font-extrabold">{title}</h2>
      {children}
    </section>
  );
}

const num = (n: number) => ltr(String(n));

function Targets(p: Props) {
  const { t, formatDate } = useI18n();
  const care = p.care!;
  const message = (code?: string) => code && t(`chronicCare.error.${code as CareError}`);
  const limitText = (x: CareTarget) => t(`chronicCare.target.${x.direction}`, { limit: num(x.limit), unit: x.unit });
  return (
    <Card title={t('chronicCare.targets.title')}>
      <ul className="flex flex-col divide-y">
        {care.targets.map(x => {
          const measure = t(`patient360.code.${x.code}`);
          return (
            <li key={x.code} className="flex flex-col gap-1 py-2">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <strong>{measure}</strong>
                <span className="inline-flex items-center gap-1.5 text-sm font-bold">
                  {x.met === null ? <CircleMinus aria-hidden className="size-4 shrink-0" /> : x.met ? <CircleCheck aria-hidden className="size-4 shrink-0" />
                    : <CircleAlert aria-hidden className="size-4 shrink-0 text-danger-700" />}
                  {t(x.met === null ? 'chronicCare.status.none' : x.met ? 'chronicCare.status.met' : 'chronicCare.status.notMet')}
                </span>
              </div>
              <span className="text-sm">
                {x.latest === null ? t('chronicCare.target.noReading')
                  : t('chronicCare.target.latest', { value: num(x.latest), unit: x.unit, date: formatDate(new Date(x.latestAt!), care.timeZone) })}
              </span>
              <span className="text-sm text-muted-foreground">
                {t('chronicCare.target.limit', { limit: limitText(x) })}{' · '}
                {x.isDefault ? t('chronicCare.target.default') : t('chronicCare.target.setBy', { author: x.setBy ?? '', date: formatDate(new Date(x.setAt!), care.timeZone) })}
              </span>
              {p.canDecide && (p.editing === x.code ? (
                <form noValidate onSubmit={p.onSubmitTarget} aria-label={t('chronicCare.target.form', { measure })} className="flex flex-col gap-2">
                  <Field id={`target-${x.code}`} label={t('chronicCare.target.limitLabel', { measure, unit: x.unit })} error={message(p.targetForm.formState.errors.limit?.message)}>
                    <Input id={`target-${x.code}`} inputMode="decimal" dir="ltr" autoComplete="off" className="h-11"
                      {...describedBy(`target-${x.code}`, !!p.targetForm.formState.errors.limit)} {...p.targetForm.register('limit')} />
                  </Field>
                  {p.targetFailed && <p role="alert" className={fieldError}>{t('chronicCare.target.failed')}</p>}
                  <div className="flex flex-wrap gap-2">
                    <Button type="submit" className="min-h-11" loading={p.savingTarget}>{t('chronicCare.target.save')}</Button>
                    <Button type="button" variant="outline" className="min-h-11" onClick={p.onCancelTarget}>{t('chronicCare.cancel')}</Button>
                  </div>
                </form>
              ) : (
                <div><Button variant="outline" className="min-h-11" aria-label={t('chronicCare.target.changeFor', { measure })} onClick={() => p.onEditTarget(x.code, x.limit)}>{t('chronicCare.target.change')}</Button></div>
              ))}
            </li>
          );
        })}
      </ul>
      {!care.rulesSignedOff && <p className="text-sm text-muted-foreground">{t('chronicCare.rulesDraft')}</p>}
    </Card>
  );
}

function Gaps(p: Props) {
  const { t, formatDate } = useI18n();
  const care = p.care!;
  // A day (DateOnly) is shown as written: midnight UTC formatted in UTC never moves to the day before.
  const day = (d: string) => formatDate(new Date(d), 'UTC');
  const title = (g: CareGap) => t(`chronicCare.gap.${g.rule}`, { test: g.item, vaccine: g.vaccine ?? '', dose: g.dose ?? 0 });
  const detail = (g: CareGap) =>
    g.rule === 'OrderNotResulted' ? t('chronicCare.detail.ordered', { date: day(g.since!) })
    : g.rule === 'VaccineDue' ? t('chronicCare.detail.dueFrom', { date: day(g.since!) })
    : g.rule === 'FallsReview' ? t('chronicCare.detail.fall', { date: day(g.since!) })
    : g.rule === 'GlucoseToleranceTest' ? t('chronicCare.detail.ogtt')
    : g.lastDone ? t('chronicCare.detail.lastDone', { date: day(g.lastDone) }) : t('chronicCare.detail.never');
  return (
    <Card title={t('chronicCare.gaps.title')}>
      {care.gaps.length === 0 ? <p className="text-muted-foreground">{t('chronicCare.gaps.empty')}</p> : (
        <ul className="flex flex-col divide-y">
          {care.gaps.map(g => {
            const k = gapKey(g);
            const name = title(g);
            const kind = checkKindOf(g);
            const outcome = p.outcome?.key === k ? p.outcome.outcome : null;
            const errors = p.checkForm.formState.errors;
            return (
              <li key={k} className="flex flex-col gap-2 py-2">
                <span className="flex items-start gap-2">
                  <CircleAlert aria-hidden className="mt-0.5 size-4 shrink-0" />
                  <span className="flex min-w-0 flex-col">
                    <strong>{name}</strong>
                    <span className="text-sm text-muted-foreground">{detail(g)}</span>
                  </span>
                </span>
                {p.canDecide && (
                  <div className="flex flex-wrap gap-2">
                    <Button variant="outline" className="min-h-11" loading={!!p.acting && gapKey(p.acting) === k}
                      aria-label={t('chronicCare.action.for', { action: t(`chronicCare.action.${g.action}`), gap: name })} onClick={() => p.onAct(g)}>
                      {t(`chronicCare.action.${g.action}`)}
                    </Button>
                    {kind && p.checking !== k && (
                      <Button variant="outline" className="min-h-11" aria-label={t('chronicCare.check.recordFor', { gap: name })} onClick={() => p.onCheck(g)}>{t('chronicCare.check.record')}</Button>
                    )}
                  </div>
                )}
                <div aria-live="polite">{outcome && <p role="status" className="text-sm font-bold">{t(`chronicCare.outcome.${outcome}`)}</p>}</div>
                {kind && p.checking === k && (
                  <form noValidate onSubmit={p.onSubmitCheck(g)} aria-label={t('chronicCare.check.form', { gap: name })} className="flex flex-col gap-2">
                    <Field id={`check-${g.rule}`} label={t('chronicCare.check.doneOn')} error={errors.doneOn?.message && t(`chronicCare.error.${errors.doneOn.message as CareError}`)}>
                      <Input id={`check-${g.rule}`} type="date" dir="ltr" className="h-11" {...describedBy(`check-${g.rule}`, !!errors.doneOn)} {...p.checkForm.register('doneOn')} />
                    </Field>
                    {p.checkFailed && <p role="alert" className={fieldError}>{t('chronicCare.check.failed')}</p>}
                    <div className="flex flex-wrap gap-2">
                      <Button type="submit" className="min-h-11" loading={p.savingCheck}>{t('chronicCare.check.save')}</Button>
                      <Button type="button" variant="outline" className="min-h-11" onClick={p.onCancelCheck}>{t('chronicCare.cancel')}</Button>
                    </div>
                  </form>
                )}
              </li>
            );
          })}
        </ul>
      )}
      {!care.rulesSignedOff && <p className="text-sm text-muted-foreground">{t('chronicCare.rulesDraft')}</p>}
    </Card>
  );
}

function Goals(p: Props) {
  const { t, formatDate } = useI18n();
  const care = p.care!;
  const message = (code?: string) => code && t(`chronicCare.error.${code as CareError}`);
  const gErrors = p.goalForm.formState.errors;
  return (
    <Card title={t('chronicCare.goals.title')}>
      {care.goals.length === 0 ? <p className="text-muted-foreground">{t('chronicCare.goals.empty')}</p> : (
        <ul className="flex flex-col gap-4">
          {care.goals.map(g => (
            <li key={g.id} className="flex flex-col gap-1">
              <strong>{g.text}</strong>
              {g.target && <span className="text-sm">{g.target}</span>}
              <div role="progressbar" aria-label={g.text} aria-valuemin={0} aria-valuemax={100} aria-valuenow={g.progressPercent} className="h-2 overflow-hidden rounded bg-muted">
                <div className="h-full bg-primary" style={{ width: `${g.progressPercent}%` }} />
              </div>
              <span className="text-sm text-muted-foreground">
                {t('chronicCare.goals.meta', { percent: num(g.progressPercent), author: g.createdBy ?? '', updatedBy: g.updatedBy ?? '', date: formatDate(new Date(g.updatedAt), care.timeZone) })}
              </span>
              {p.canDecide && (p.moving === g.id ? (
                <form noValidate onSubmit={p.onSubmitMove} aria-label={t('chronicCare.goals.moveFor', { goal: g.text })} className="flex flex-col gap-2">
                  <Field id={`goal-${g.id}`} label={t('chronicCare.goals.progressLabel')} error={message(p.progressForm.formState.errors.progress?.message)}>
                    <Input id={`goal-${g.id}`} inputMode="numeric" dir="ltr" autoComplete="off" className="h-11"
                      {...describedBy(`goal-${g.id}`, !!p.progressForm.formState.errors.progress)} {...p.progressForm.register('progress')} />
                  </Field>
                  {p.progressFailed && <p role="alert" className={fieldError}>{t('chronicCare.goals.failed')}</p>}
                  <div className="flex flex-wrap gap-2">
                    <Button type="submit" className="min-h-11" loading={p.savingProgress}>{t('chronicCare.goals.saveProgress')}</Button>
                    <Button type="button" variant="outline" className="min-h-11" onClick={p.onCancelMove}>{t('chronicCare.cancel')}</Button>
                  </div>
                </form>
              ) : (
                <div><Button variant="outline" className="min-h-11" aria-label={t('chronicCare.goals.moveFor', { goal: g.text })} onClick={() => p.onMove(g.id, g.progressPercent)}>{t('chronicCare.goals.move')}</Button></div>
              ))}
            </li>
          ))}
        </ul>
      )}
      {p.canDecide && (p.adding ? (
        <form noValidate onSubmit={p.onSubmitGoal} aria-label={t('chronicCare.goals.form')} className="flex flex-col gap-3 border-t pt-3">
          <Field id="goal-text" label={t('chronicCare.goals.text')} error={message(gErrors.text?.message)}>
            <Input id="goal-text" className="h-11" {...describedBy('goal-text', !!gErrors.text)} {...p.goalForm.register('text')} />
          </Field>
          <Field id="goal-target" label={t('chronicCare.goals.target')} error={message(gErrors.target?.message)}>
            <Input id="goal-target" className="h-11" {...describedBy('goal-target', !!gErrors.target)} {...p.goalForm.register('target')} />
          </Field>
          <Field id="goal-progress" label={t('chronicCare.goals.progressLabel')} error={message(gErrors.progress?.message)}>
            <Input id="goal-progress" inputMode="numeric" dir="ltr" autoComplete="off" className="h-11" {...describedBy('goal-progress', !!gErrors.progress)} {...p.goalForm.register('progress')} />
          </Field>
          {p.goalFailed && <p role="alert" className={fieldError}>{t('chronicCare.goals.failed')}</p>}
          <div className="flex flex-wrap gap-2">
            <Button type="submit" className="min-h-11" loading={p.savingGoal}>{t('chronicCare.goals.save')}</Button>
            <Button type="button" variant="outline" className="min-h-11" onClick={p.onCancelGoal}>{t('chronicCare.cancel')}</Button>
          </div>
        </form>
      ) : <div><Button variant="outline" className="min-h-11" onClick={p.onAddGoal}>{t('chronicCare.goals.add')}</Button></div>)}
    </Card>
  );
}
