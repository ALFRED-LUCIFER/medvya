import type { FormEventHandler } from 'react';
import type { UseFormReturn } from 'react-hook-form';
import { useI18n } from '@medvya/i18n';
import AllergiesSection from './AllergiesSection';
import VitalsForm, { type VitalsFormProps } from './VitalsForm';
import { isVitalsField, problemKey, recordable, type AllergyForm, type AllergyStatus, type IntakeScreen } from '../domain/intake';

type Props = Omit<VitalsFormProps, 'vitals' | 'timeZone' | 'readOnly' | 'withComplaint'> & {
  screen: IntakeScreen | null; loading: boolean; refused: boolean; failed: boolean;
  allergyForm: UseFormReturn<AllergyForm>; settingAllergy: string | null; addingAllergy: boolean; confirmingNone: boolean;
  onAllergyStatus: (id: string, status: AllergyStatus) => void; onAddAllergy: FormEventHandler<HTMLFormElement>; onNoKnownAllergies: () => void;
  /** The guided steps have no Vitals section of their own: the entry brings its heading. */
  titled?: boolean;
};

/**
 * Vitals, weight and allergies recorded by the doctor in the consultation (MDV audit P0-2), so a clinic without a nurse step can still run the
 * allergy check and weigh a child. The same forms and server rules as nurse intake; after the consultation ends they are read only.
 */
export default function ConsultEntry(p: Props) {
  const { t } = useI18n();
  if (p.loading) return <p role="status" className="text-sm">{t('intake.loading')}</p>;
  if (p.refused) return null; // a role without vitals entry sees the note's vitals line only
  if (p.failed || !p.screen) return <p role="alert" className="text-sm font-bold text-danger-700">{t('intake.loadFailed')}</p>;
  const { intake, timeZone } = p.screen;
  const readOnly = !recordable(intake.state);
  return (
    <section aria-labelledby={p.titled ? 'consult-entry-title' : undefined} className="flex flex-col gap-3">
      {p.titled && <h2 id="consult-entry-title" className="text-lg font-extrabold">{t('intake.vitals')}</h2>}
      <VitalsForm {...p} vitals={intake.vitals} timeZone={timeZone} readOnly={readOnly} withComplaint={false} />
      <AllergiesSection allergies={intake.allergies} noKnownConfirmed={intake.noKnownAllergiesConfirmed} form={p.allergyForm} readOnly={readOnly} online={p.online}
        settingId={p.settingAllergy} adding={p.addingAllergy} confirmingNone={p.confirmingNone} onStatus={p.onAllergyStatus} onAdd={p.onAddAllergy} onNone={p.onNoKnownAllergies}
        invalid={p.problem?.kind === 'invalid' ? p.problem : null} />
      {p.problem && (p.problem.kind !== 'invalid' || (!isVitalsField(p.problem.field) && p.problem.field !== 'substance'))
        && <p role="alert" className="text-sm font-semibold text-danger-700">{t(problemKey(p.problem))}</p>}
    </section>
  );
}
