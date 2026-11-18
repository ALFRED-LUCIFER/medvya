import { Link } from 'react-router';
import { ltr, useI18n } from '@medvya/i18n';
import {
  Button, buttonVariants, Card, PageContainer, PageHeader, PageMessage, Chip, ChoiceRow, Field, FilterChip, Input, Section, SegmentedControl, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Textarea, ToothChart,
} from '@medvya/ui';
import { CircleAlert, CircleCheck, Clock, Ban, CircleDot, FileDown, Lock } from 'lucide-react';
import { fieldError } from '../../settings/components/fieldStyles';
import { ChartBanner } from '../../patient360/components/Patient360Page';
import type { Patient360 } from '../../patient360/domain/chart';
import { byPhase, conditions, conditionTone, dentitions, historyQuestions, marksFor, money, nextSteps, surfaceConditions, surfaceOrder, type DentalView, type PlanItem, type PlanStatus } from '../domain/dental';

const statusLook: Record<PlanStatus, { tone: 'info' | 'warning' | 'success' | 'neutral'; icon: typeof Clock }> = {
  Planned: { tone: 'info', icon: CircleDot }, InProgress: { tone: 'warning', icon: Clock }, Done: { tone: 'success', icon: CircleCheck }, Declined: { tone: 'neutral', icon: Ban },
};

/**
 * The Dental tab of the patient record (MDV-P8-011, MDV-P8-012; design DN-02 tooth chart and DN-03 plan and estimate): the chart drawn facing the patient,
 * the picked tooth's state and history with a new finding, the plan by phase with each item's next step, the estimate and the patient's acceptance, and
 * the printed chart and estimate. A finding is never edited (a correction is a newer finding). Changing anything needs a doctor who signs notes; the
 * server decides again on every call. The rules are a draft until a dentist signs them, and the screen says so.
 */
export default function DentalPage(p: DentalView & { consultPath: string | null; patient: Patient360 | null }) {
  const { t, formatDate, formatTime, formatMoney } = useI18n();
  const header = <PageHeader title={t('dental.title')} />;
  if (p.loading) return <PageContainer>{header}<PageMessage kind="busy">{t('dental.loading')}</PageMessage></PageContainer>;
  if (p.unavailable) return <PageContainer>{header}<PageMessage kind="empty">{t('dental.unavailable')}</PageMessage></PageContainer>;
  if (p.forbidden) return <PageContainer>{header}<PageMessage kind="denied" title={t('patient360.refused.title')}>{t('patient360.refused.role')}</PageMessage></PageContainer>;
  if (p.failed || !p.chart) return <PageContainer>{header}<PageMessage kind="error">{t('dental.loadFailed')}</PageMessage></PageContainer>;
  const c = p.chart;
  const when = (at: string) => `${formatDate(new Date(at), c.timeZone)} ${formatTime(new Date(at), c.timeZone)}`;
  const amount = (minor: number, currency: string | null) => money(minor, currency ?? 'INR', formatMoney);

  return (
    <PageContainer>
      {header}
      {/* Identity and allergy in words above every clinical write (DESIGN.md non-negotiable 1). */}
      {p.patient && <ChartBanner data={p.patient} />}
      <Card title={t('dental.chart.title')}
        actions={<div className="flex flex-wrap gap-2">
          {p.consultPath && <Link to={p.consultPath} className={buttonVariants({ variant: 'ghost' })}>{t('dental.openConsult')}</Link>}
          <Button variant="outline" loading={p.printing} onClick={p.onPrint}><FileDown aria-hidden />{t('dental.print')}</Button>
        </div>}>
        {p.printFailed && <p role="alert" className={fieldError}>{t('dental.printFailed')}</p>}
        <SegmentedControl label={t('dental.chart.dentition')} value={p.dentition} onValueChange={p.onDentition}
          options={dentitions.map(d => ({ value: d, label: t(`dental.dentition.${d}`) }))} />
        <ToothChart dentition={p.dentition} marks={marksFor(c.teeth, t)} notRecorded={t('dental.notRecorded')} selected={p.tooth} onSelect={p.onTooth} />
        <p className="text-caption text-muted-foreground">{t(c.rulesSignedOff ? 'dental.rulesSigned' : 'dental.rulesDraft')}</p>
      </Card>

      <MedicalHistoryCard {...p} when={when} />

      <div className="grid gap-4 wide:grid-cols-2">
        <ToothPanel {...p} when={when} />
        <PlanCard {...p} amount={amount} when={when} />
      </div>
    </PageContainer>
  );
}

/**
 * The medical history check before treatment (MDV-P8-014): when it was last confirmed and by whom, each answer that raises a risk in words, and, in the
 * doctor's open consultation, the six questions to ask the patient again. Treatment on the plan starts only once it is confirmed on this visit.
 */
function MedicalHistoryCard(p: DentalView & { when: (at: string) => string }) {
  const { t } = useI18n();
  const check = p.chart!.historyCheck;
  return (
    <Card title={t('dental.medical.title')}>
      <p>{check ? t('dental.medical.lastConfirmed', { who: check.author ?? '', when: p.when(check.confirmedAt) }) : t('dental.medical.never')}</p>
      {check && (check.yes.length > 0 ? (
        <div className="flex flex-col gap-1 rounded-md border border-warning-border bg-warning-bg p-3 text-warning-text">
          <p className="flex items-center gap-2 font-bold"><CircleAlert aria-hidden className="size-4 shrink-0" />{t('dental.medical.risks')}</p>
          <ul className="list-disc ps-6">{check.yes.map(q => <li key={q}>{t(`dental.medical.question.${q}`)}</li>)}</ul>
        </div>
      ) : <p className="flex items-center gap-2"><CircleCheck aria-hidden className="size-4 shrink-0" />{t('dental.medical.noRisks')}</p>)}
      {check?.note && <p>{t('dental.medical.noteShown', { note: check.note })}</p>}
      {p.canChange && p.encounterId && (p.historyOnVisit
        ? <p role="status" className="flex items-center gap-2 font-bold"><CircleCheck aria-hidden className="size-4 shrink-0" />{t('dental.medical.thisVisit')}</p>
        : <form className="flex flex-col gap-3" onSubmit={e => { e.preventDefault(); p.onConfirmHistory(); }}>
            <p className="font-bold">{t('dental.medical.ask')}</p>
            <div className="grid gap-x-6 gap-y-2 wide:grid-cols-2">
              {historyQuestions.map(q => (
                <fieldset key={q} className="flex flex-col gap-1">
                  <legend className="text-label font-bold">{t(`dental.medical.question.${q}`)}</legend>
                  <div className="flex gap-4">
                    <ChoiceRow type="radio" name={`history-${q}`} checked={p.answers[q] === true} onChange={() => p.onAnswer(q, true)}>{t('dental.medical.yes')}</ChoiceRow>
                    <ChoiceRow type="radio" name={`history-${q}`} checked={p.answers[q] === false} onChange={() => p.onAnswer(q, false)}>{t('dental.medical.no')}</ChoiceRow>
                  </div>
                </fieldset>
              ))}
            </div>
            <Field id="dental-history-note" label={t('dental.medical.note')}>
              <Textarea value={p.historyNote} maxLength={500} onChange={e => p.onHistoryNote(e.target.value)} />
            </Field>
            {p.historyIncomplete && <p role="alert" className={fieldError}>{t('dental.medical.answerAll')}</p>}
            {p.historyFailed && <p role="alert" className={fieldError}>{t('dental.medical.failed')}</p>}
            <div><Button type="submit" loading={p.historySaving}>{t('dental.medical.confirm')}</Button></div>
          </form>)}
    </Card>
  );
}

function ToothPanel(p: DentalView & { consultPath: string | null; when: (at: string) => string }) {
  const { t } = useI18n();
  if (!p.tooth) return <Card title={t('dental.history.title', { tooth: '' }).trim()}><p className="text-muted-foreground">{t('dental.tooth.pick')}</p></Card>;
  const current = p.current;
  return (
    <Card title={t('dental.tooth.title', { tooth: p.tooth })}>
      <p className="font-bold">
        {current
          ? t(current.surfaces ? 'dental.tooth.stateSurfaces' : 'dental.tooth.state', { state: t(`dental.condition.${current.condition}`), surfaces: current.surfaces ?? '' })
          : t('dental.tooth.state', { state: t('dental.notRecorded') })}
      </p>
      <Section title={t('dental.history.title', { tooth: p.tooth })}>
        {p.history.length === 0 ? <p className="text-muted-foreground">{t('dental.history.empty')}</p> : (
          <ul className="flex flex-col divide-y divide-border">
            {p.history.map(f => (
              <li key={f.id} className="flex items-start gap-2 py-2">
                <Lock aria-hidden className="mt-0.5 size-4 shrink-0 text-muted-foreground" />
                <span className="flex flex-col">
                  <strong>{f.surfaces ? `${t(`dental.condition.${f.condition}`)} (${f.surfaces})` : t(`dental.condition.${f.condition}`)}</strong>
                  {f.note && <span>{f.note}</span>}
                  <span className="text-caption text-muted-foreground">{t('dental.history.by', { who: f.author ?? '', when: p.when(f.recordedAt) })}</span>
                </span>
              </li>
            ))}
          </ul>
        )}
        <p className="text-caption text-muted-foreground">{t('dental.finding.correction')}</p>
      </Section>
      {p.canChange && <FindingForm {...p} />}
    </Card>
  );
}

function FindingForm(p: DentalView & { consultPath: string | null }) {
  const { t } = useI18n();
  if (!p.encounterId) return (
    <Section title={t('dental.finding.title')}>
      <p>{t('dental.finding.noConsult')}</p>
    </Section>
  );
  const takesSurfaces = p.condition !== null && surfaceConditions.has(p.condition);
  const problem = p.findingProblem === 'surfaces:required' ? t('dental.error.surfaces.required') : null;
  return (
    <Section title={t('dental.finding.title')}>
      <form className="flex flex-col gap-3" onSubmit={e => { e.preventDefault(); p.onAddFinding(); }}>
        <fieldset className="flex flex-col gap-1">
          <legend className="text-label font-bold">{t('dental.finding.condition')}</legend>
          <div className="grid grid-cols-2 gap-x-4">
            {conditions.map(c => (
              <ChoiceRow key={c} type="radio" name="condition" value={c} checked={p.condition === c} onChange={() => p.onCondition(c)}>
                <span className="flex items-center gap-2">
                  <Chip size="sm" tone={conditionTone[c]} label={t(`dental.condition.abbr.${c}`)} aria-hidden />
                  {t(`dental.condition.${c}`)}
                </span>
              </ChoiceRow>
            ))}
          </div>
        </fieldset>
        {takesSurfaces && (
          <fieldset className="flex flex-col gap-1" aria-describedby="dental-surfaces-hint">
            <legend className="text-label font-bold">{t('dental.finding.surfaces')}</legend>
            <div className="flex flex-wrap gap-2">
              {surfaceOrder.map(s => <FilterChip key={s} on={p.surfaces.includes(s)} label={`${s} · ${t(`dental.surface.${s}`)}`} onClick={() => p.onSurface(s)} />)}
            </div>
            <p id="dental-surfaces-hint" className="text-caption text-muted-foreground">{t('dental.finding.surfacesHint')}</p>
            {problem && <p role="alert" className={fieldError}>{problem}</p>}
          </fieldset>
        )}
        <Field id="dental-note" label={t('dental.finding.note')}>
          <Textarea value={p.note} maxLength={200} onChange={e => p.onNote(e.target.value)} />
        </Field>
        {p.findingError && <p role="alert" className={fieldError}>{t(p.findingError)}</p>}
        {p.findingFailed && <p role="alert" className={fieldError}>{t('dental.finding.failed')}</p>}
        {p.findingAdded && <p role="status" className="flex items-center gap-2"><CircleCheck aria-hidden className="size-4" />{t('dental.finding.added', { tooth: p.findingAdded })}</p>}
        <div><Button type="submit" loading={p.findingSaving} disabled={p.findingProblem !== null}>{t('dental.finding.add', { tooth: p.tooth ?? '' })}</Button></div>
      </form>
    </Section>
  );
}

function PlanCard(p: DentalView & { consultPath: string | null; amount: (minor: number, currency: string | null) => string; when: (at: string) => string }) {
  const { t } = useI18n();
  const plan = p.chart!.plan;
  return (
    <Card title={t('dental.plan.title')}>
      {plan.items.length === 0 ? <p className="text-muted-foreground">{t('dental.plan.empty')}</p> : byPhase(plan.items).map(g => (
        <Section key={g.phase} title={t('dental.plan.phase', { phase: String(g.phase) })}>
          <ul className="flex flex-col divide-y divide-border">
            {g.items.map(item => <PlanRow key={item.id} item={item} {...p} />)}
          </ul>
        </Section>
      ))}
      {p.moveConflict && <p role="alert" className={fieldError}>{t('dental.plan.conflict')}</p>}
      {p.moveError && <p role="alert" className={fieldError}>{t(p.moveError)}</p>}
      {p.moveFailed && <p role="alert" className={fieldError}>{t('dental.plan.failed')}</p>}
      {p.canChange && !p.encounterId && plan.items.some(i => i.status === 'Planned' || i.status === 'InProgress') && (
        <p className="flex items-center gap-2 text-muted-foreground"><CircleAlert aria-hidden className="size-4 shrink-0" />{t('dental.plan.needsConsult')}</p>
      )}
      {p.canChange && p.encounterId && !p.historyOnVisit && plan.items.some(i => i.status === 'Planned') && (
        <p className="flex items-center gap-2 text-muted-foreground"><CircleAlert aria-hidden className="size-4 shrink-0" />{t('dental.plan.needsHistory')}</p>
      )}
      {p.canChange && <AddItem {...p} />}
      <Section title={t('dental.estimate.title')}>
        <p className="text-heading font-bold">{t('dental.estimate.total', { total: ltr(p.amount(plan.totalMinor, plan.currency)) })}</p>
        <p className="text-caption text-muted-foreground">{t('dental.estimate.note')}</p>
        {plan.acceptance
          ? <p className="flex items-start gap-2">
              {plan.acceptanceCurrent ? <CircleCheck aria-hidden className="mt-0.5 size-4 shrink-0" /> : <CircleAlert aria-hidden className="mt-0.5 size-4 shrink-0 text-warning-700" />}
              <span className="flex flex-col">
                <span>{t('dental.acceptance.accepted', { name: plan.acceptance.acceptedBy, method: t(`dental.acceptance.method.${plan.acceptance.method}`), when: p.when(plan.acceptance.acceptedAt) })}</span>
                {!plan.acceptanceCurrent && <strong>{t('dental.acceptance.changed')}</strong>}
              </span>
            </p>
          : <p>{t('dental.acceptance.none')}</p>}
        {p.canChange && plan.items.some(i => i.status !== 'Declined') && (!plan.acceptance || !plan.acceptanceCurrent) && <AcceptForm {...p} />}
      </Section>
    </Card>
  );
}

function PlanRow({ item, ...p }: DentalView & { item: PlanItem; amount: (minor: number, currency: string | null) => string }) {
  const { t } = useI18n();
  const look = statusLook[item.status];
  const steps = p.canChange ? nextSteps(item.status) : [];
  return (
    <li className="flex flex-col gap-2 py-3">
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
        <span className="rounded-sm border border-border px-1.5 font-mono text-caption font-bold">{item.tooth ? `${item.tooth}${item.surfaces ? ` ${item.surfaces}` : ''}` : t('dental.plan.wholeMouth')}</span>
        <strong className="flex-1">{item.serviceName}</strong>
        <Chip tone={look.tone} icon={look.icon} label={t(`dental.plan.status.${item.status}`)} />
        <span className="font-mono font-bold"><bdi dir="ltr">{p.amount(item.estimateMinor, item.currency)}</bdi></span>
      </div>
      {item.reason && <p className="text-caption">{t('dental.plan.reason', { reason: item.reason })}</p>}
      {item.status === 'Done' && <p className="text-caption text-muted-foreground">{t('dental.plan.billed')}</p>}
      {p.declining === item.id ? (
        <form className="flex flex-col gap-2" onSubmit={e => { e.preventDefault(); p.onStep(item, 'decline'); }}>
          <Field id={`decline-${item.id}`} label={t('dental.plan.declineReason')}>
            <Input value={p.reason} maxLength={200} onChange={e => p.onReason(e.target.value)} />
          </Field>
          <div className="flex flex-wrap gap-2">
            <Button type="submit" variant="outline" loading={p.moving === item.id} disabled={!p.reason.trim()}>{t('dental.plan.declineConfirm')}</Button>
            <Button type="button" variant="ghost" onClick={() => p.onDecline(null)}>{t('dental.plan.cancel')}</Button>
          </div>
        </form>
      ) : steps.length > 0 && (
        <div className="flex flex-wrap gap-2">
          {steps.map(step => step === 'decline'
            ? <Button key={step} variant="ghost" onClick={() => p.onDecline(item.id)}>{t('dental.plan.decline')}</Button>
            : <Button key={step} variant="outline" loading={p.moving === item.id} disabled={((step === 'start' || step === 'done') && !p.encounterId) || (step === 'start' && !p.historyOnVisit)}
                onClick={() => p.onStep(item, step)}>{t(`dental.plan.${step}`)}</Button>)}
        </div>
      )}
    </li>
  );
}

function AddItem(p: DentalView & { amount: (minor: number, currency: string | null) => string }) {
  const { t } = useI18n();
  if (!p.servicesLoading && p.services.length === 0) return <p className="text-muted-foreground">{t('dental.plan.noServices')}</p>;
  return (
    <Section title={t('dental.plan.add.title')}>
      <form className="flex flex-col gap-3" onSubmit={e => { e.preventDefault(); p.onAddItem(); }}>
        <Select value={p.serviceId} onValueChange={p.onService}>
          <Field id="dental-service" label={t('dental.plan.add.service')}>
            <SelectTrigger className="w-full"><SelectValue placeholder={t('dental.plan.add.servicePlaceholder')} /></SelectTrigger>
          </Field>
          <SelectContent>
              {p.services.map(s => (
                <SelectItem key={s.id} value={s.id} disabled={s.priceMinor === null}>
                  {s.priceMinor === null ? t('dental.plan.add.unpriced', { name: s.name }) : `${s.name} · ${p.amount(s.priceMinor, s.currency)}`}
                </SelectItem>
              ))}
          </SelectContent>
        </Select>
        <div className="grid grid-cols-2 gap-3">
          <Field id="dental-plan-tooth" label={t('dental.plan.add.tooth')} hint={t('dental.plan.add.toothHint')}>
            <Input inputMode="numeric" maxLength={2} value={p.planTooth ?? ''} onChange={e => p.onPlanTooth(e.target.value.trim() || null)} dir="ltr" />
          </Field>
          <Select value={String(p.phase)} onValueChange={v => p.onPhase(Number(v))}>
            <Field id="dental-plan-phase" label={t('dental.plan.add.phase')}>
              <SelectTrigger className="w-full"><SelectValue /></SelectTrigger>
            </Field>
            <SelectContent>{[1, 2, 3, 4, 5, 6, 7, 8, 9].map(n => <SelectItem key={n} value={String(n)}>{t('dental.plan.phase', { phase: String(n) })}</SelectItem>)}</SelectContent>
          </Select>
        </div>
        {p.planError && <p role="alert" className={fieldError}>{t(p.planError)}</p>}
        {p.planFailed && <p role="alert" className={fieldError}>{t('dental.plan.failed')}</p>}
        <div><Button type="submit" variant="outline" loading={p.planSaving} disabled={!p.serviceId}>{t('dental.plan.add.submit')}</Button></div>
      </form>
    </Section>
  );
}

function AcceptForm(p: DentalView) {
  const { t } = useI18n();
  return (
    <form className="flex flex-col gap-3" onSubmit={e => { e.preventDefault(); p.onAccept(); }}>
      <Field id="dental-accepted-by" label={t('dental.acceptance.name')}>
        <Input value={p.acceptedBy} maxLength={120} autoComplete="off" onChange={e => p.onAcceptedBy(e.target.value)} />
      </Field>
      <fieldset className="flex flex-col gap-1">
        <legend className="text-label font-bold">{t('dental.acceptance.method')}</legend>
        {(['OnTablet', 'PrintedAndSigned'] as const).map(m => (
          <ChoiceRow key={m} type="radio" name="acceptance-method" value={m} checked={p.method === m} onChange={() => p.onMethod(m)}>{t(`dental.acceptance.method.${m}`)}</ChoiceRow>
        ))}
      </fieldset>
      {p.acceptError && <p role="alert" className={fieldError}>{t(p.acceptError)}</p>}
      {p.acceptFailed && <p role="alert" className={fieldError}>{t('dental.plan.failed')}</p>}
      <div><Button type="submit" variant="outline" loading={p.accepting} disabled={!p.acceptedBy.trim()}>{t('dental.acceptance.record')}</Button></div>
    </form>
  );
}

