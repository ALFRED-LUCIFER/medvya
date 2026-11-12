import type { FormEventHandler, ReactNode } from 'react';
import type { UseFormReturn } from 'react-hook-form';
import { Link } from 'react-router';
import { CircleAlert, CircleSlash, TrendingDown, CircleX, FilePen, History, Plus, Printer, ShieldCheck, UserRound } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, buttonVariants, ClinicalAlert, Input, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Switch } from '@medvya/ui';
import { isCurrent, type AuthoriseProblem, type RxAuthorisationState } from '../domain/authorisation';
import { describedBy, fieldError } from '../../settings/components/fieldStyles';
import { Field } from '../../settings/components/Field';
import { Section } from '../../settings/components/Section';
import { lineSafety } from '../domain/checks';
import SafetyChecks, { SafetyChip, type SafetyProps } from './SafetyChecks';
import {
  directionOf, doseUnits, frequencies, printLanguages, routes, timings,
  type Medicine, type PrintLanguage, type RxLine, type RxProblem, type SaveState, type SigDraft, type SigField,
} from '../domain/prescription';

type Props = SafetyProps & {
  ended: boolean;
  lines: RxLine[];
  loading: boolean;
  failed: boolean;
  add: UseFormReturn<{ medicine: string }>;
  results: Medicine[];
  searching: boolean;
  language: PrintLanguage; onLanguage: (l: PrintLanguage) => void;
  problem: RxProblem | null;
  busy: boolean;
  draftOf: (l: RxLine) => SigDraft;
  saveOf: (id: string) => SaveState | null;
  invalidOf: (l: RxLine) => SigField[];
  onAddMedicine: (id: string) => void;
  onAddFreeText: FormEventHandler<HTMLFormElement>;
  onInclude: (id: string, included: boolean) => void;
  onField: (l: RxLine, field: SigField, value: string) => void;
  authorisation: RxAuthorisationState | null;
  authorising: boolean;
  authProblem: AuthoriseProblem | null;
  onAuthorise: () => void;
  cancelling: boolean;
  cancelForm: UseFormReturn<{ reason: string }>;
  cancelPending: boolean;
  cancelProblem: 'reauth' | 'failed' | null;
  onCancelling: (open: boolean) => void;
  onCancel: FormEventHandler<HTMLFormElement>;
  onConfirmIdentity: () => void;
  printPath: string;
  /** The Add medicine panel (MDV-P2-039), opened from here while the consultation is open. */
  addMedicine?: ReactNode;
};

const none = 'none';
const sourceIcon = { AddedByYou: UserRound, AddedByColleague: UserRound, ChartReconciliation: History } as const;

/**
 * Prescription composer (E-05, E-11): medicines from the formulary or typed as free text (flagged), each with a dose builder that autosaves,
 * the readable instruction in the chosen print language, and discontinue lines from the reconciliation. Authorising (MDV-P2-017) is its own action here.
 * The safety checks (MDV-P2-016) follow the lines, and each line carries its safety chip.
 */
export default function PrescriptionPanel(p: Props) {
  const { t } = useI18n();
  const prescribed = p.lines.filter(l => l.kind === 'Prescribe');
  // Stops, changes and tapers follow the reconciliation (MDV-P2-011, MDV-P2-043): read here, decided there.
  const stopped = p.lines.filter(l => l.kind !== 'Prescribe');
  const status = p.loading ? t('rx.loading') : p.failed ? t('rx.loadFailed') : p.lines.length === 0 ? t('rx.empty') : null;

  return (
    <Section title={t('rx.title')} intro={t(p.ended ? 'rx.ended' : 'rx.intro')}>
      <div className="flex flex-wrap items-end gap-3">
        <Field id="rx-language" label={t('rx.language')} className="min-w-56">
          <Select value={p.language} onValueChange={v => p.onLanguage(v as PrintLanguage)}>
            <SelectTrigger id="rx-language" className="h-11 w-full"><SelectValue /></SelectTrigger>
            <SelectContent>{printLanguages.map(l => <SelectItem key={l} value={l}>{t(`rx.lang.${l}`)}</SelectItem>)}</SelectContent>
          </Select>
        </Field>
        <AuthorisationState state={p.authorisation} />
        {!p.ended && p.addMedicine}
      </div>

      {status && <p role="status">{status}</p>}

      {prescribed.length > 0 && (
        <ul aria-label={t('rx.listLabel')} className="flex flex-col gap-4">
          {prescribed.map(l => <Line key={l.id} line={l} {...p} />)}
        </ul>
      )}

      {stopped.length > 0 && (
        <ul aria-label={t('rx.stopLabel')} className="flex flex-col gap-3">
          {stopped.map(l => (
            <li key={l.medicine} className="flex flex-col gap-1 rounded-lg border p-4">
              <p className="flex flex-wrap items-center gap-2">
                <span className="inline-flex items-center gap-1.5 font-bold">
                  {l.kind === 'Discontinue' ? <CircleSlash aria-hidden className="size-4" /> : <TrendingDown aria-hidden className="size-4" />}
                  {t(l.kind === 'Change' ? 'rx.change' : l.kind === 'Taper' ? 'rx.taper' : 'rx.discontinue')}
                </span>
                <span className="font-bold">{l.medicine}</span>
                <SourceChip line={l} />
              </p>
              {l.reason && <p className="text-sm">{t('rx.reason', { reason: l.reason })}</p>}
              <Sig line={l} language={p.language} />
            </li>
          ))}
        </ul>
      )}

      {prescribed.length > 0 && <SafetyChecks {...p} />}

      {p.lines.length > 0 && <Authorise {...p} />}

      {!p.ended && (
        <form noValidate onSubmit={p.onAddFreeText} className="flex flex-col gap-3 border-t pt-4">
          <h3 className="font-bold">{t('rx.add.title')}</h3>
          <Field id="rx-medicine" label={t('rx.add.search')} hint={t('rx.add.hint')} error={p.add.formState.errors.medicine && t('rx.error.required')}>
            <Input id="rx-medicine" className="h-11" autoComplete="off" {...describedBy('rx-medicine', !!p.add.formState.errors.medicine, true)} {...p.add.register('medicine')} />
          </Field>
          {p.results.length > 0 && (
            <ul aria-label={t('rx.add.results')} className="flex flex-col gap-2">
              {p.results.map(m => (
                <li key={m.id}>
                  <Button type="button" variant="outline" className="min-h-11 w-full justify-start" disabled={p.busy} onClick={() => p.onAddMedicine(m.id)}>
                    <Plus aria-hidden className="size-4" />{t('rx.add.medicine', { generic: m.generic, strength: m.strength, form: m.form })}
                  </Button>
                </li>
              ))}
            </ul>
          )}
          {p.searching && <p role="status" className="text-sm text-muted-foreground">{t('rx.add.searching')}</p>}
          <Button type="submit" variant="outline" className="min-h-11 self-start" disabled={p.busy}>{t('rx.add.freeText')}</Button>
        </form>
      )}

      {p.problem && <p role="alert" className={fieldError}>{t(`rx.error.${p.problem}`)}</p>}
    </Section>
  );
}

/** Draft, authorised (version and the number on the paper), cancelled, or changed since: an icon and words, never colour alone. */
function AuthorisationState({ state }: { state: RxAuthorisationState | null }) {
  const { t } = useI18n();
  const a = state?.authorised;
  const [Icon, text] = !a ? [FilePen, t('rx.draftState')]
    : a.cancellation ? [CircleX, t('rx.auth.cancelled', { version: a.versionNo, number: a.number })]
    : state.changed ? [CircleAlert, t('rx.auth.changed')]
    : [ShieldCheck, t('rx.auth.authorised', { version: a.versionNo, number: a.number })];
  return <p role="status" className="flex items-center gap-2 text-sm font-bold"><Icon aria-hidden className="size-4" />{text}</p>;
}

/** Authorising (MDV-P2-017): its own action after the safety checks, separate from signing the note. Printing follows an unchanged authorisation. */
function Authorise(p: Props) {
  const { t } = useI18n();
  const state = p.authorisation;
  const current = isCurrent(state);
  const cancellation = state?.authorised?.cancellation;
  const reasonError = p.cancelForm.formState.errors.reason;
  return (
    <div className="flex flex-col gap-2 border-t pt-4">
      <p className="text-sm">{t('rx.auth.separate')}</p>
      {(p.checks?.unresolved ?? 0) > 0 && <p className="text-sm font-bold">{t('rx.auth.unresolved')}</p>}
      {cancellation && <p className="text-sm">{t('rx.auth.cancelledReason', { reason: cancellation.reason })}</p>}
      <div className="flex flex-wrap gap-3">
        {!current && (
          <Button className="min-h-11" loading={p.authorising} disabled={p.busy} onClick={p.onAuthorise}>
            <ShieldCheck aria-hidden className="size-4" />{t(cancellation ? 'rx.auth.reissue' : state?.authorised ? 'rx.auth.again' : 'rx.auth.authorise')}
          </Button>
        )}
        {current && <Link to={p.printPath} className={buttonVariants({ variant: 'outline', className: 'min-h-11' })}><Printer aria-hidden className="size-4" />{t('rx.auth.print')}</Link>}
        {current && !p.cancelling && (
          <Button variant="outline" className="min-h-11" onClick={() => p.onCancelling(true)}><CircleX aria-hidden className="size-4" />{t('rx.auth.cancel')}</Button>
        )}
      </div>
      {current && p.cancelling && (
        <form noValidate onSubmit={p.onCancel} className="flex flex-col gap-2">
          <Field id="rx-cancel-reason" label={t('rx.auth.cancelReason')} hint={t('rx.auth.cancelHint')} error={reasonError && t('rx.auth.cancelReasonRequired')}>
            <Input id="rx-cancel-reason" className="h-11" autoComplete="off" {...describedBy('rx-cancel-reason', !!reasonError, true)} {...p.cancelForm.register('reason')} />
          </Field>
          <div className="flex flex-wrap gap-2">
            <Button type="submit" className="min-h-11" loading={p.cancelPending}>{t('rx.auth.cancelConfirm')}</Button>
            <Button type="button" variant="outline" className="min-h-11" onClick={() => p.onCancelling(false)}>{t('rx.auth.keep')}</Button>
          </div>
        </form>
      )}
      {p.cancelProblem === 'reauth' && (
        <div role="alert" className="flex flex-wrap items-center gap-3">
          <p className="font-bold">{t('rx.auth.cancelReauth')}</p>
          <Button variant="outline" className="min-h-11" onClick={p.onConfirmIdentity}>{t('rx.auth.confirm')}</Button>
        </div>
      )}
      {p.cancelProblem === 'failed' && <p role="alert" className={fieldError}>{t('rx.auth.cancelFailed')}</p>}
      {p.authProblem === 'reauth' && (
        <div role="alert" className="flex flex-wrap items-center gap-3">
          <p className="font-bold">{t('rx.auth.reauth')}</p>
          <Button variant="outline" className="min-h-11" onClick={p.onConfirmIdentity}>{t('rx.auth.confirm')}</Button>
        </div>
      )}
      {p.authProblem && p.authProblem !== 'reauth' && <p role="alert" className={fieldError}>{t(`rx.auth.error.${p.authProblem}`)}</p>}
    </div>
  );
}

function SourceChip({ line }: { line: RxLine }) {
  const { t } = useI18n();
  const Icon = sourceIcon[line.source];
  return <span className="inline-flex items-center gap-1 rounded-full border px-2 py-0.5 text-xs font-bold"><Icon aria-hidden className="size-3.5" />{t(`rx.source.${line.source}`)}</span>;
}

function Sig({ line, language }: { line: RxLine; language: PrintLanguage }) {
  const { t } = useI18n();
  const text = line.sig[language];
  return (
    <p className="text-sm">
      <span className="me-1 font-bold">{t('rx.instruction', { language: t(`rx.lang.${language}`) })}</span>
      {text ? <span lang={language} dir={directionOf(language)}>{text}</span> : <span className="text-muted-foreground">{t('rx.instructionEmpty')}</span>}
    </p>
  );
}

function Line({ line, ended, busy, language, checks, draftOf, saveOf, invalidOf, onInclude, onField }: Props & { line: RxLine }) {
  const { t } = useI18n();
  const id = line.id ?? '';
  const draft = draftOf(line);
  const invalid = invalidOf(line);
  const save = saveOf(id);
  const locked = ended || !line.included;
  const fieldId = (f: SigField) => `rx-${id}-${f}`;
  const text = (f: 'dose' | 'durationDays' | 'quantity' | 'instructions', className = '') => (
    <Field id={fieldId(f)} label={t(`rx.field.${f}`)} error={invalid.includes(f) ? t(`rx.invalid.${f}`) : null} className={className}>
      <Input id={fieldId(f)} className="h-11" autoComplete="off" inputMode={f === 'dose' ? 'decimal' : f === 'instructions' ? undefined : 'numeric'} disabled={locked} value={draft[f]}
        {...describedBy(fieldId(f), invalid.includes(f))} onChange={e => onField(line, f, e.target.value)} />
    </Field>
  );
  const pick = (f: 'doseUnit' | 'route' | 'frequency' | 'timing', options: { value: string; label: string }[]) => (
    <Field id={fieldId(f)} label={t(`rx.field.${f}`)}>
      <Select value={draft[f] || none} disabled={locked} onValueChange={v => onField(line, f, v === none ? '' : v)}>
        <SelectTrigger id={fieldId(f)} className="h-11 w-full"><SelectValue /></SelectTrigger>
        <SelectContent>
          <SelectItem value={none}>{t('rx.notGiven')}</SelectItem>
          {options.map(o => <SelectItem key={o.value} value={o.value}>{o.label}</SelectItem>)}
        </SelectContent>
      </Select>
    </Field>
  );

  return (
    <li aria-label={line.medicine} className="flex flex-col gap-3 rounded-lg border p-4">
      <div className="flex flex-wrap items-center gap-2">
        <span className="font-bold">{line.medicine}</span>
        {(line.strength || line.form) && <span className="text-sm">{[line.strength, line.form].filter(Boolean).join(' · ')}</span>}
        <SourceChip line={line} />
        {checks && line.included && <SafetyChip state={lineSafety(checks, id)} />}
        {!line.included && <span className="text-sm font-bold">{t('rx.notIncluded')}</span>}
        {!ended && <Switch className="ms-auto" checked={line.included} disabled={busy} aria-label={t('rx.include', { name: line.medicine })} onCheckedChange={on => onInclude(id, on)} />}
      </div>
      {line.freeText && <ClinicalAlert severity="attention">{t('rx.freeTextWarning')}</ClinicalAlert>}
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        {text('dose')}
        {pick('doseUnit', doseUnits.map(c => ({ value: c, label: t(`rx.doseUnit.${c}`) })))}
        {pick('route', routes.map(c => ({ value: c, label: t(`rx.route.${c}`) })))}
        {pick('frequency', frequencies.map(c => ({ value: c, label: t(`rx.frequency.${c}`) })))}
        {pick('timing', timings.map(c => ({ value: c, label: t(`rx.timing.${c}`) })))}
        {text('durationDays')}
        {text('quantity')}
        {text('instructions', 'sm:col-span-2 lg:col-span-1')}
      </div>
      <Sig line={line} language={language} />
      {save && save !== 'invalid' && <p role="status" className={save === 'saving' || save === 'saved' ? 'text-sm text-muted-foreground' : fieldError}>{t(save === 'saving' || save === 'saved' ? `rx.save.${save}` : `rx.error.${save}`)}</p>}
    </li>
  );
}
