import type { FormEventHandler } from 'react';
import { Controller, type UseFormReturn } from 'react-hook-form';
import { CircleHelp } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, Input, Label, Switch } from '@medvya/ui';
import type { MedicineForm, MedicineView } from '../domain/intake';

type Props = { medicines: MedicineView[]; form: UseFormReturn<MedicineForm>; readOnly: boolean; online: boolean; adding: boolean; onAdd: FormEventHandler<HTMLFormElement> };

/**
 * Medicines the patient says they take (MDV-P2-008): patient-reported, never confirmed here. A name the nurse could not identify ("a white tablet
 * for sugar") is written as the patient said it and marked "Needs clarification" for the doctor.
 */
export default function MedicinesSection(p: Props) {
  const { t } = useI18n();
  const { errors } = p.form.formState;
  return (
    <section aria-labelledby="intake-medicines" className="flex flex-col gap-3 rounded-xl border bg-card p-4">
      <h2 id="intake-medicines" className="text-lg font-extrabold">{t('intake.medicine.title')}</h2>
      {p.medicines.length === 0 ? <p className="text-muted-foreground">{t('intake.medicine.none')}</p> : (
        <ul className="flex flex-col gap-2">
          {p.medicines.map(m => (
            <li key={m.id} className="flex flex-col gap-1 rounded-md border p-3">
              <p className="font-bold">{m.name}</p>
              <p className="text-sm text-muted-foreground">{[m.dose, m.frequency, t('intake.medicine.patientReported')].filter(Boolean).join(' · ')}</p>
              {m.needsClarification && <p className="inline-flex items-center gap-1.5 text-sm font-bold"><CircleHelp aria-hidden className="size-4" />{t('intake.medicine.needsClarification')}</p>}
            </li>
          ))}
        </ul>
      )}
      {!p.readOnly && (
        <form noValidate onSubmit={p.onAdd} className="flex flex-col gap-3 border-t pt-3" aria-label={t('intake.medicine.add')}>
          <div className="grid gap-3 sm:grid-cols-3">
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="medicine-name" className="font-bold">{t('intake.medicine.name')}</Label>
              <Input id="medicine-name" autoComplete="off" maxLength={100} aria-invalid={!!errors.name} {...p.form.register('name')} />
              {errors.name && <p role="alert" className="text-sm font-semibold text-danger-700">{t('intake.medicine.nameRequired')}</p>}
            </div>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="medicine-dose" className="font-bold">{t('intake.medicine.dose')}</Label>
              <Input id="medicine-dose" autoComplete="off" maxLength={50} {...p.form.register('dose')} />
            </div>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="medicine-frequency" className="font-bold">{t('intake.medicine.frequency')}</Label>
              <Input id="medicine-frequency" autoComplete="off" maxLength={50} {...p.form.register('frequency')} />
            </div>
          </div>
          <div className="flex min-h-11 items-center gap-3">
            <Controller control={p.form.control} name="needsClarification" render={({ field }) => (
              <Switch id="medicine-clarify" checked={field.value} onCheckedChange={field.onChange} />
            )} />
            <Label htmlFor="medicine-clarify" className="min-h-11 flex-1">{t('intake.medicine.needsClarification')}</Label>
          </div>
          <Button type="submit" className="self-start" loading={p.adding} disabled={!p.online}>{t('intake.medicine.add')}</Button>
        </form>
      )}
    </section>
  );
}
