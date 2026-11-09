import type { FormEventHandler } from 'react';
import type { UseFormReturn } from 'react-hook-form';
import { Link } from 'react-router';
import { CircleAlert, CircleCheck, CircleHelp } from 'lucide-react';
import { ltr, useI18n } from '@medvya/i18n';
import { Button, buttonVariants, Input, Label, Table, TableBody, TableCell, TableHead, TableHeader, TableRow, TableState } from '@medvya/ui';
import type { ClinicalRefusal } from '../../clinical-access/domain/clinicalAccess';
import type { EncounterBanner } from '../../encounters/domain/encounter';
import IdentityBanner from '../../encounters/components/IdentityBanner';
import { canConfirm, disagreeing, type DecisionKind, type Draft, type MedicineRow, type MedicineSide, type Problem, type Reconciliation } from '../domain/reconciliation';

type Props = {
  view: Reconciliation | null; banner: EncounterBanner | null; loading: boolean; notFound: boolean; refusal: ClinicalRefusal | null; failed: boolean;
  form: UseFormReturn<Draft>; open: { key: string; kind: DecisionKind } | null; problem: Problem | null; saving: boolean; confirming: boolean; online: boolean;
  onPick: (key: string, kind: DecisionKind) => void; onCancel: () => void; onSave: FormEventHandler<HTMLFormElement>; onConfirm: () => void;
  consultPath: string; boardPath: string;
};

/**
 * Medication reconciliation (E-07, MDV-P2-011): each medicine across the chart, what the patient said at this visit and other sources, with a
 * conflict written in words, and one decision per row. Confirm stays disabled until every row has one; it then updates the chart.
 */
export default function ReconciliationPage(p: Props) {
  const { t, formatTime } = useI18n();
  if (p.loading) return <p role="status">{t('reconcile.loading')}</p>;
  if (p.refusal) return <Message title={t('consult.refused.title')} body={t(p.refusal.canRequest ? 'consult.refused.ask' : 'consult.refused.role')} {...p} />;
  if (!p.view || p.notFound || p.failed) return <Message title={t(p.notFound ? 'consult.notFound' : 'reconcile.loadFailed')} {...p} />;
  const { view } = p;
  const confirmed = view.confirmedAt !== null;
  return (
    <div className="flex flex-col gap-4">
      {p.banner && <IdentityBanner banner={p.banner} />}
      <section aria-labelledby="reconcile-title" className="flex flex-col gap-3 rounded-xl border bg-card p-4">
        <h2 id="reconcile-title" className="text-lg font-extrabold">{t('reconcile.title')}</h2>
        <p className="text-sm text-muted-foreground">{t('reconcile.intro')}</p>
        <p role="status" className="font-bold">{t('reconcile.summary', { decided: view.decided, total: view.total, disagree: view.disagree })}</p>
        <TableState state={view.rows.length === 0 ? 'empty' : 'ready'} messages={{ loading: t('reconcile.loading'), error: t('reconcile.loadFailed'), empty: t('reconcile.empty') }}>
          <Table label={t('reconcile.tableLabel')} className="min-w-[900px]">
            <TableHeader>
              <TableRow>{(['medicine', 'chart', 'patient', 'other', 'conflict', 'decision'] as const).map(c => <TableHead key={c} scope="col">{t(`reconcile.col.${c}`)}</TableHead>)}</TableRow>
            </TableHeader>
            <TableBody>
              {view.rows.map(r => (
                <TableRow key={r.key}>
                  <TableCell className="font-bold">{r.name}</TableCell>
                  <TableCell><SideText side={r.chart} /></TableCell>
                  <TableCell><SideText side={r.patient} /></TableCell>
                  <TableCell><SideText side={r.other} /></TableCell>
                  <TableCell><ConflictText row={r} /></TableCell>
                  <TableCell className="min-w-64"><DecisionCell row={r} {...p} locked={confirmed} /></TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableState>
        <div className="flex flex-wrap items-center gap-3">
          {confirmed
            ? <p role="status" className="inline-flex items-center gap-1.5 font-bold"><CircleCheck aria-hidden className="size-4" />{t('reconcile.confirmedAt', { time: formatTime(new Date(view.confirmedAt!), p.banner?.timeZone) })}</p>
            : <Button className="min-h-11" loading={p.confirming} disabled={!canConfirm(view) || !p.online} aria-describedby="reconcile-confirm-note" onClick={p.onConfirm}>{t('reconcile.confirm')}</Button>}
          <Link to={p.consultPath} className={buttonVariants({ variant: 'outline', className: 'min-h-11' })}>{t('reconcile.backToConsult')}</Link>
        </div>
        {!confirmed && <p id="reconcile-confirm-note" className="text-sm text-muted-foreground">{t(canConfirm(view) ? 'reconcile.confirmReady' : 'reconcile.confirmBlocked')}</p>}
        {p.problem && <p role="alert" className="font-bold text-danger-700">{t(`reconcile.error.${p.problem}`)}</p>}
      </section>
    </div>
  );
}

function SideText({ side }: { side: MedicineSide | null }) {
  const { t } = useI18n();
  if (!side) return <span className="text-muted-foreground">{t('reconcile.notListed')}</span>;
  return (
    <span className="flex flex-col gap-0.5">
      <span>{[side.dose ? ltr(side.dose) : t('reconcile.doseBlank'), side.frequency, t(`reconcile.state.${side.state}`)].filter(Boolean).join(' · ')}</span>
      {side.needsClarification && <span className="inline-flex items-center gap-1.5 text-sm font-bold"><CircleHelp aria-hidden className="size-4" />{t('intake.medicine.needsClarification')}</span>}
    </span>
  );
}

/** The disagreement as a sentence, e.g. "atorvastatin active in the chart, patient says stopped": words and an icon, never colour alone. */
function ConflictText({ row }: { row: MedicineRow }) {
  const { t } = useI18n();
  if (!row.conflict) return <span className="inline-flex items-center gap-1.5"><CircleCheck aria-hidden className="size-4" />{t('reconcile.conflict.none')}</span>;
  const { who, side } = disagreeing(row);
  const text = row.conflict === 'State' && row.chart && side
    ? t('reconcile.conflict.state', { name: row.name, chart: t(`reconcile.state.${row.chart.state}`), who: t(`reconcile.who.${who}`), other: t(`reconcile.state.${side.state}`) })
    : row.conflict === 'Dose' ? t('reconcile.conflict.dose', { name: row.name })
    : t('reconcile.conflict.notOnChart', { name: row.name, who: t(`reconcile.who.${who}`) });
  return <span className="inline-flex items-start gap-1.5 font-semibold"><CircleAlert aria-hidden className="mt-0.5 size-4 shrink-0" />{text}</span>;
}

function DecisionCell(p: Props & { row: MedicineRow; locked: boolean }) {
  const { t } = useI18n();
  const { row } = p;
  const opening = p.open?.key === row.key ? p.open : null;
  const { errors } = p.form.formState;
  if (p.locked) return <span>{row.decision ? [t(`reconcile.decision.${row.decision.kind}`), row.decision.newDose && ltr(row.decision.newDose), row.decision.reason].filter(Boolean).join(' · ') : t('reconcile.notListed')}</span>;
  return (
    <div className="flex flex-col gap-2">
      <div role="group" aria-label={t('reconcile.decisionFor', { name: row.name })} className="flex flex-wrap gap-2">
        {row.allowed.map(k => (
          <Button key={k} type="button" size="sm" variant={(opening?.kind ?? row.decision?.kind) === k ? 'default' : 'outline'} className="min-h-11"
            aria-pressed={(opening?.kind ?? row.decision?.kind) === k} disabled={p.saving || !p.online} onClick={() => p.onPick(row.key, k)}>{t(`reconcile.decision.${k}`)}</Button>
        ))}
      </div>
      {row.decision && !opening && (
        <p className="text-sm">
          {[t('reconcile.decided', { decision: t(`reconcile.decision.${row.decision.kind}`) }), row.decision.newDose && ltr(row.decision.newDose), row.decision.reason].filter(Boolean).join(' · ')}
        </p>
      )}
      {opening && (
        <form noValidate onSubmit={p.onSave} aria-label={t('reconcile.decisionFor', { name: row.name })} className="flex flex-col gap-2 rounded-md border p-3">
          {opening.kind === 'Change' && (
            <div className="flex flex-col gap-1.5">
              <Label htmlFor={`dose-${row.key}`} className="font-bold">{t('reconcile.newDose')}</Label>
              <Input id={`dose-${row.key}`} autoComplete="off" maxLength={100} className="h-11" aria-invalid={!!errors.newDose} {...p.form.register('newDose')} />
              {errors.newDose && <p role="alert" className="text-sm font-semibold text-danger-700">{t('reconcile.newDoseRequired')}</p>}
            </div>
          )}
          <div className="flex flex-col gap-1.5">
            <Label htmlFor={`reason-${row.key}`} className="font-bold">{t('reconcile.reason')}</Label>
            <Input id={`reason-${row.key}`} autoComplete="off" maxLength={500} className="h-11" aria-invalid={!!errors.reason} {...p.form.register('reason')} />
            {errors.reason && <p role="alert" className="text-sm font-semibold text-danger-700">{t('reconcile.reasonRequired')}</p>}
          </div>
          <div className="flex gap-2">
            <Button type="submit" className="min-h-11" loading={p.saving} disabled={!p.online}>{t('reconcile.save')}</Button>
            <Button type="button" variant="outline" className="min-h-11" onClick={p.onCancel}>{t('reconcile.cancel')}</Button>
          </div>
        </form>
      )}
    </div>
  );
}

function Message({ title, body, boardPath }: { title: string; body?: string; boardPath: string }) {
  const { t } = useI18n();
  return (
    <div role="alert" className="flex flex-col items-start gap-3 rounded-xl border bg-card p-6">
      <h1 className="text-lg font-extrabold">{title}</h1>
      {body && <p>{body}</p>}
      <Link to={boardPath} className={buttonVariants({ variant: 'outline', className: 'min-h-11' })}>{t('consult.backToBoard')}</Link>
    </div>
  );
}
