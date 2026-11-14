import type { FormEventHandler } from 'react';
import { Link } from 'react-router';
import type { UseFormReturn } from 'react-hook-form';
import { TriangleAlert } from 'lucide-react';
import { useI18n, type MessageKey } from '@medvya/i18n';
import { Button, buttonVariants, Label, Textarea, PageContainer, PatientBanner } from '@medvya/ui';
import { bannerAllergyState } from '../../patient360/domain/chart';
import AllergiesSection from './AllergiesSection';
import MedicinesSection from './MedicinesSection';
import VitalsForm from './VitalsForm';
import {
  allergyBlockers, chartAllergies, isVitalsField, problemKey, type IntakeForm, type AllergyForm, type AllergyStatus, type IntakeProblem, type IntakeScreen, type MedicineForm, type VitalKind,
} from '../domain/intake';

type Props = {
  screen: IntakeScreen | null; loading: boolean; notFound: boolean; refused: boolean; failed: boolean;
  form: UseFormReturn<IntakeForm>;
  escalationForm: UseFormReturn<{ reason: string }>;
  allergyForm: UseFormReturn<AllergyForm>; medicineForm: UseFormReturn<MedicineForm>;
  settingAllergy: string | null; addingAllergy: boolean; confirmingNone: boolean; addingMedicine: boolean;
  onAllergyStatus: (id: string, status: AllergyStatus) => void; onAddAllergy: FormEventHandler<HTMLFormElement>; onNoKnownAllergies: () => void; onAddMedicine: FormEventHandler<HTMLFormElement>;
  problem: IntakeProblem | null; badKind: VitalKind | null;
  saving: boolean; keeping: VitalKind | null; escalating: boolean; sendingEscalation: boolean; handingOff: boolean; dirty: boolean; online: boolean;
  onSave: FormEventHandler<HTMLFormElement>; onKeep: (kind: VitalKind) => void; onRemeasure: (kind: VitalKind) => void;
  onToggleEscalate: () => void; onEscalate: FormEventHandler<HTMLFormElement>; onHandOff: () => void;
  queuePath: string;
};

const fieldError = 'text-sm font-semibold text-danger-700';

/**
 * N-01 Nurse intake (MDV-P2-007): identity check, the chief complaint in the patient's words, large vitals inputs with the previous value, out-of-range
 * flags in words with an icon (never colour alone) that need a re-measure or an explicit "keep", escalation to the doctor with a reason, and the handoff,
 * which stays disabled with its reason while anything blocks it. Built for a tablet: every target is at least 44 px.
 */
export default function IntakePage(p: Props) {
  const { t } = useI18n();
  if (p.loading) return <p role="status">{t('intake.loading')}</p>;
  if (p.refused || p.notFound || p.failed || !p.screen)
    return <Message title={t(p.refused ? 'intake.refused' : p.notFound ? 'intake.notFound' : 'intake.loadFailed')} queuePath={p.queuePath} />;
  const { intake, patient, timeZone } = p.screen;
  const withNurse = intake.state === 'WithNurse';
  const readOnly = !withNurse;
  const blocked = intake.blockers.length > 0 || p.dirty;
  // Every reason the handoff waits, in words: the allergy gate first, as it is the one that must never be skipped.
  const reasons: MessageKey[] = [
    ...(allergyBlockers.some(b => intake.blockers.includes(b)) ? (['intake.handOff.blockedAllergies'] as const) : []),
    ...(intake.blockers.includes('vitals_undecided') ? (['intake.handOff.blockedVitals'] as const) : []),
    ...(p.dirty ? (['intake.handOff.unsaved'] as const) : []),
  ];

  return (
    <PageContainer width="reading">
      <Identity patient={patient} intake={intake} />
      {readOnly && <p role="status" className="font-bold">{t('intake.handedOff')}</p>}
      <VitalsForm form={p.form} vitals={intake.vitals} timeZone={timeZone} readOnly={readOnly} online={p.online} problem={p.problem} badKind={p.badKind}
        saving={p.saving} keeping={p.keeping} onSave={p.onSave} onKeep={p.onKeep} onRemeasure={p.onRemeasure} withComplaint />

      <AllergiesSection allergies={intake.allergies} noKnownConfirmed={intake.noKnownAllergiesConfirmed} form={p.allergyForm} readOnly={readOnly} online={p.online}
        settingId={p.settingAllergy} adding={p.addingAllergy} confirmingNone={p.confirmingNone} onStatus={p.onAllergyStatus} onAdd={p.onAddAllergy} onNone={p.onNoKnownAllergies}
        invalid={p.problem?.kind === 'invalid' ? p.problem : null} />
      <MedicinesSection medicines={intake.medicines} form={p.medicineForm} readOnly={readOnly} online={p.online} adding={p.addingMedicine} onAdd={p.onAddMedicine} />

      {p.problem && (p.problem.kind !== 'invalid' || (!isVitalsField(p.problem.field) && p.problem.field !== 'substance')) && <p role="alert" className={fieldError}>{t(problemKey(p.problem))}</p>}
      {intake.escalated && <p role="status" className="inline-flex items-center gap-1.5 font-bold"><TriangleAlert aria-hidden className="size-4" />{t('intake.escalated')}</p>}

      {withNurse && (
        <div className="flex flex-col gap-3 rounded-xl border bg-card p-4">
          <div className="flex flex-wrap items-center gap-3">
            <Button variant="outline" aria-expanded={p.escalating} onClick={p.onToggleEscalate}>{t('intake.escalate')}</Button>
            <Button variant="default" className="whitespace-normal" loading={p.handingOff} disabled={blocked || !p.online} aria-describedby={blocked ? 'intake-handoff-why' : undefined} onClick={p.onHandOff}>
              {t('intake.handOff')}
            </Button>
            <Link to={p.queuePath} className={buttonVariants({ variant: 'outline' })}>{t('intake.back')}</Link>
          </div>
          {blocked && <ul id="intake-handoff-why" className="text-sm font-semibold">{reasons.map(r => <li key={r}>{t(r)}</li>)}</ul>}
          {p.escalating && (
            <form noValidate onSubmit={p.onEscalate} className="flex flex-col gap-2">
              <Label htmlFor="intake-reason" className="font-bold">{t('intake.escalate.reason')}</Label>
              <Textarea id="intake-reason" rows={2} maxLength={500} className="min-h-14 text-base" aria-invalid={!!p.escalationForm.formState.errors.reason}
                aria-describedby={p.escalationForm.formState.errors.reason ? 'intake-reason-error' : undefined} {...p.escalationForm.register('reason')} />
              {p.escalationForm.formState.errors.reason && <p id="intake-reason-error" className={fieldError}>{t('intake.escalate.required')}</p>}
              <Button type="submit" className="self-start" loading={p.sendingEscalation} disabled={!p.online}>{t('intake.escalate.send')}</Button>
            </form>
          )}
        </div>
      )}
      {readOnly && <Link to={p.queuePath} className={buttonVariants({ variant: 'outline', className: 'self-start' })}>{t('intake.back')}</Link>}
    </PageContainer>
  );
}

/** The patient strip pinned under the top bar while the nurse scrolls (MDV-P1-092), with the allergy as recorded so far; then the identity check. */
function Identity({ patient, intake }: { patient: IntakeScreen['patient']; intake: IntakeScreen['intake'] }) {
  const { t, formatCalendarDate } = useI18n();
  return (
    <>
      <PatientBanner variant="strip" pinned headingLevel={1} patient={{
        name: patient.fullName, age: patient.ageYears, sex: patient.sexAtBirth, mrn: patient.mrn, dob: formatCalendarDate(patient.dateOfBirth),
        allergy: bannerAllergyState(chartAllergies(intake), s => t(`patient360.allergyStatus.${s}`)),
      }} />
      <p className="text-sm text-muted-foreground">{t('intake.identity.check')}</p>
    </>
  );
}

function Message({ title, queuePath }: { title: string; queuePath: string }) {
  const { t } = useI18n();
  return (
    <div role="alert" className="flex flex-col items-start gap-3 rounded-xl border bg-card p-6">
      <h1 className="text-lg font-extrabold">{title}</h1>
      <Link to={queuePath} className={buttonVariants({ variant: 'outline' })}>{t('intake.back')}</Link>
    </div>
  );
}
