import type { FormEventHandler } from 'react';
import type { UseFormReturn } from 'react-hook-form';
import { CircleSlash, FilePen, History, Plus, UserRound } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, ClinicalAlert, Input, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Switch } from '@medvya/ui';
import { describedBy, fieldError } from '../../settings/components/fieldStyles';
import { Field } from '../../settings/components/Field';
import { Section } from '../../settings/components/Section';
import {
  directionOf, doseUnits, frequencies, printLanguages, routes, timings,
  type Medicine, type PrintLanguage, type RxLine, type RxProblem, type SaveState, type SigDraft, type SigField,
} from '../domain/prescription';

type Props = {
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
};

const none = 'none';
const sourceIcon = { AddedByYou: UserRound, AddedByColleague: UserRound, ChartReconciliation: History } as const;

/**
 * Prescription composer (E-05, E-11): medicines from the formulary or typed as free text (flagged), each with a dose builder that autosaves,
 * the readable instruction in the chosen print language, and discontinue lines from the reconciliation. A draft only: authorising is separate.
 */
export default function PrescriptionPanel(p: Props) {
  const { t } = useI18n();
  const prescribed = p.lines.filter(l => l.kind === 'Prescribe');
  const stopped = p.lines.filter(l => l.kind === 'Discontinue');
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
        <p className="flex items-center gap-2 text-sm font-bold"><FilePen aria-hidden className="size-4" />{t('rx.draftState')}</p>
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
                <span className="inline-flex items-center gap-1.5 font-bold"><CircleSlash aria-hidden className="size-4" />{t('rx.discontinue')}</span>
                <span className="font-bold">{l.medicine}</span>
                <SourceChip line={l} />
              </p>
              {l.reason && <p className="text-sm">{t('rx.reason', { reason: l.reason })}</p>}
              <Sig line={l} language={p.language} />
            </li>
          ))}
        </ul>
      )}

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

function Line({ line, ended, busy, language, draftOf, saveOf, invalidOf, onInclude, onField }: Props & { line: RxLine }) {
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
