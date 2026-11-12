import type { UseFormReturn } from 'react-hook-form';
import { CircleAlert, CircleCheck, CircleHelp, Package, Pill, Plus } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, ClinicalAlert, Dialog, DialogContent, DialogDescription, DialogTitle, DialogTrigger, Input } from '@medvya/ui';
import { describedBy, fieldError } from '../../settings/components/fieldStyles';
import { Field } from '../../settings/components/Field';
import { allergyStatusOf, bannerAllergies } from '../../patient360/domain/chart';
import type { AllergyState, Allergy } from '../../patient360/domain/chart';
import type { SexAtBirth } from '../../patients/domain/directory';
import { howLong, howOften, whenTaken, type Choice } from '../domain/addMedicine';
import { directionOf, printLanguages, type Medicine, type PrintLanguage, type RxLine, type RxProblem } from '../domain/prescription';
import SafetyChecks, { type SafetyProps } from './SafetyChecks';

/** Who the medicine is for: shown at the top of the panel, with the allergy state in words (never colour alone). */
export type AddMedicinePatient = { fullName: string; ageYears: number; sexAtBirth: SexAtBirth; allergies: { state: AllergyState; allergies: Allergy[] } };

type Props = SafetyProps & {
  patient: AddMedicinePatient | null;
  open: boolean; encounterId: string | null; refused: boolean;
  onOpen: () => void; onClose: () => void;
  search: UseFormReturn<{ medicine: string }>; results: Medicine[]; searching: boolean; noResults: boolean;
  medicine: Medicine | null; strengths: Medicine[]; onMedicine: (m: Medicine) => void;
  choice: Choice; onChoice: (c: Partial<Choice>) => void; instructionsInvalid: boolean;
  line: RxLine | null; quantity: number | 'pack' | null; saving: boolean;
  problem: RxProblem | null; blocked: boolean; chosen: boolean; canAdd: boolean; onAdd: () => void; added: string | null;
};

/**
 * Add medicine (MDV-P2-039, E-05 / E-11, design `dw.med`): a side panel from the chart's Medicines card or the consultation. It reuses the
 * composer's draft, sig and safety checks; the server decides who may prescribe and what the checks find.
 */
export default function AddMedicinePanel(p: Props) {
  const { t, locale } = useI18n();
  const language: PrintLanguage = (printLanguages as readonly string[]).includes(locale) ? (locale as PrintLanguage) : 'en';
  return (
    // The trigger is Radix's, so closing (Escape, the close button, Cancel) hands focus back to it.
    <Dialog open={p.open} onOpenChange={o => (o ? p.onOpen() : p.onClose())}>
      <div className="flex flex-col items-start gap-2">
        <DialogTrigger asChild><Button variant="outline" className="min-h-11"><Plus aria-hidden className="size-4" />{t('rx.addMed.open')}</Button></DialogTrigger>
        {p.added && <p role="status" className="text-sm">{t('rx.addMed.added', { name: p.added })}</p>}
      </div>
      <DialogContent closeLabel={t('rx.addMed.close')}
        className="start-auto end-0 top-0 h-dvh max-h-none w-full max-w-lg translate-x-0 content-start rounded-none rtl:translate-x-0 sm:rounded-s-xl">
        <DialogTitle>{t('rx.addMed.title')}</DialogTitle>
        <DialogDescription>{t(p.encounterId ? 'rx.addMed.intro' : 'rx.addMed.noConsult')}</DialogDescription>
        {p.patient && <Patient patient={p.patient} />}
        {p.encounterId && !p.refused && <Body {...p} language={language} />}
        {p.refused && <p role="alert" className={fieldError}>{t('rx.addMed.refused')}</p>}
      </DialogContent>
    </Dialog>
  );
}

function Patient({ patient }: { patient: AddMedicinePatient }) {
  const { t } = useI18n();
  const allergies = bannerAllergies({ allergyState: patient.allergies.state, allergies: patient.allergies.allergies });
  return (
    <section aria-label={t('rx.addMed.patient')} className="flex flex-wrap items-center gap-x-3 gap-y-1 rounded-lg border bg-muted/40 p-3 text-sm">
      <strong>{patient.fullName}</strong>
      <span>{t('consult.banner.ageSex', { age: patient.ageYears, sex: t(`patients.sex.${patient.sexAtBirth}`) })}</span>
      <span className="inline-flex items-center gap-1.5 rounded-full border px-2 py-0.5 font-bold">
        {allergies === null ? <><CircleHelp aria-hidden className="size-4" />{t('consult.banner.allergiesNotRecorded')}</>
          : allergies.length === 0 ? <><CircleCheck aria-hidden className="size-4" />{t('banner.noAllergies')}</>
          : <><CircleAlert aria-hidden className="size-4" />{t('consult.banner.allergies', { list: allergies.map(a => `${a.name} (${t(`patient360.allergyStatus.${allergyStatusOf[a.status]}`)})`).join(', ') })}</>}
      </span>
    </section>
  );
}

/** One row of quick picks: buttons that say which is chosen (aria-pressed), 44 px tall. */
function Picks<K extends string>({ id, label, options, chosen, onPick, wordOf }: { id: string; label: string; options: K[]; chosen: K | null; onPick: (k: K) => void; wordOf: (k: K) => string }) {
  return (
    <div className="flex flex-col gap-2">
      <span id={id} className="text-sm font-bold">{label}</span>
      <div role="group" aria-labelledby={id} className="flex flex-wrap gap-2">
        {options.map(k => (
          <Button key={k} type="button" variant={chosen === k ? 'default' : 'outline'} aria-pressed={chosen === k} className="min-h-11" onClick={() => onPick(k)}>{wordOf(k)}</Button>
        ))}
      </div>
    </div>
  );
}

const keys = <T extends object>(o: T) => Object.keys(o) as (keyof T & string)[];

function Body(p: Props & { language: PrintLanguage }) {
  const { t } = useI18n();
  const m = p.medicine;
  return (
    <div className="flex flex-col gap-4">
      <Field id="addmed-search" label={t('rx.addMed.search')} hint={t('rx.addMed.searchHint')}>
        <Input id="addmed-search" className="h-11" type="search" autoComplete="off" {...describedBy('addmed-search', false, true)} {...p.search.register('medicine')} />
      </Field>
      {p.searching && <p role="status" className="text-sm text-muted-foreground">{t('rx.addMed.searching')}</p>}
      {p.noResults && <p role="status" className="text-sm">{t('rx.addMed.noResults')}</p>}
      {p.results.length > 0 && (
        <ul aria-label={t('rx.addMed.results')} className="flex flex-col gap-2">
          {p.results.map(r => {
            const picked = !!m && m.generic === r.generic && m.form === r.form;
            return (
              <li key={r.id}>
                <Button type="button" variant={picked ? 'default' : 'outline'} aria-pressed={picked} disabled={p.busy} className="h-auto min-h-11 w-full flex-col items-start py-2 text-start"
                  onClick={() => p.onMedicine(r)}>
                  <span className="inline-flex items-center gap-2 font-bold"><Pill aria-hidden className="size-4" />{t('rx.addMed.result', { generic: r.generic, form: r.form })}</span>
                  {r.brands.length > 0 && <span className="text-xs">{t('rx.addMed.brands', { list: r.brands.join(', ') })}</span>}
                </Button>
              </li>
            );
          })}
        </ul>
      )}

      {m && <>
        <Picks id="addmed-strength" label={t('rx.addMed.strength')} options={p.strengths.map(s => s.id)} chosen={m.id}
          onPick={id => { const s = p.strengths.find(x => x.id === id); if (s && s.id !== m.id) p.onMedicine(s); }} wordOf={id => p.strengths.find(x => x.id === id)?.strength ?? ''} />
        <Picks id="addmed-often" label={t('rx.addMed.often')} options={keys(howOften)} chosen={p.choice.often} onPick={often => p.onChoice({ often })} wordOf={k => t(`rx.addMed.often.${k}`)} />
        <Picks id="addmed-when" label={t('rx.addMed.when')} options={keys(whenTaken)} chosen={p.choice.when} onPick={when => p.onChoice({ when })} wordOf={k => t(`rx.addMed.when.${k}`)} />
        <Picks id="addmed-long" label={t('rx.addMed.long')} options={keys(howLong)} chosen={p.choice.long} onPick={long => p.onChoice({ long })} wordOf={k => t(`rx.addMed.long.${k}`)} />
        <Field id="addmed-instructions" label={t('rx.addMed.instructions')} hint={t('rx.addMed.instructionsHint')} error={p.instructionsInvalid ? t('rx.addMed.instructionsTooLong') : null}>
          <Input id="addmed-instructions" className="h-11" autoComplete="off" value={p.choice.instructions}
            {...describedBy('addmed-instructions', p.instructionsInvalid, true)} onChange={e => p.onChoice({ instructions: e.target.value })} />
        </Field>

        {p.blocked && <ClinicalAlert severity="critical">{t('rx.addMed.blocked')}</ClinicalAlert>}
        <SafetyChecks {...p} />

        <div className="flex flex-col gap-1">
          <span className="text-sm font-bold">{t('rx.addMed.line')}</span>
          <p className="rounded-lg border border-dashed p-3">
            {p.line?.sig[p.language]
              ? <><strong>{[p.line.medicine, p.line.strength].filter(Boolean).join(' ')}</strong>{' · '}<span lang={p.language} dir={directionOf(p.language)}>{p.line.sig[p.language]}</span></>
              : <span className="text-muted-foreground">{t('rx.addMed.lineEmpty')}</span>}
          </p>
          {p.quantity !== null && (
            <p className="inline-flex items-center gap-1.5 text-sm"><Package aria-hidden className="size-4" />
              {p.quantity === 'pack' ? t('rx.addMed.quantityPack') : t('rx.addMed.quantity', { count: p.quantity })}</p>
          )}
          {p.saving && <p role="status" className="text-sm text-muted-foreground">{t('rx.addMed.saving')}</p>}
        </div>
      </>}

      {p.problem && <p role="alert" className={fieldError}>{t(`rx.error.${p.problem}`)}</p>}
      {m && !p.chosen && <p className="text-sm">{t('rx.addMed.needChoices')}</p>}
      <div className="flex flex-wrap gap-2 border-t pt-4">
        <Button className="min-h-11" disabled={!p.canAdd} onClick={p.onAdd}><Plus aria-hidden className="size-4" />{t('rx.addMed.add')}</Button>
        <Button variant="outline" className="min-h-11" onClick={p.onClose}>{t('rx.addMed.cancel')}</Button>
      </div>
    </div>
  );
}
