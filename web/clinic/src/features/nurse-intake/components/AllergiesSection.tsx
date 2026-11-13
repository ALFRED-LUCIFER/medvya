import type { FormEventHandler } from 'react';
import { Controller, type UseFormReturn } from 'react-hook-form';
import { CircleCheck, CircleHelp } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, Input, Label, Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@medvya/ui';
import { allergyStatuses, newAllergyStatuses, type AllergyForm, type AllergyStatus, type AllergyView } from '../domain/intake';

type Props = {
  allergies: AllergyView[]; noKnownConfirmed: boolean; form: UseFormReturn<AllergyForm>; readOnly: boolean; online: boolean;
  settingId: string | null; adding: boolean; confirmingNone: boolean;
  onStatus: (id: string, status: AllergyStatus) => void; onAdd: FormEventHandler<HTMLFormElement>; onNone: () => void;
  /** The server's code for the field it refused (e.g. "duplicate"). */
  invalid: { field: string; code: string } | null;
};

const fieldError = 'text-sm font-semibold text-danger-700';

/**
 * Allergies at nurse intake (MDV-P2-008). Every allergy must be set to Confirmed, Patient reported, Unknown or Not an allergy; one with no status
 * says so in words and keeps the handoff disabled. "No known allergies" is never assumed: with none on record the nurse confirms it on purpose.
 */
export default function AllergiesSection(p: Props) {
  const { t } = useI18n();
  const { errors } = p.form.formState;
  const refused = (field: string) => (p.invalid?.field === field ? p.invalid.code : null);
  return (
    <section aria-labelledby="intake-allergies" className="flex flex-col gap-3 rounded-xl border bg-card p-4">
      <h2 id="intake-allergies" className="text-lg font-extrabold">{t('intake.allergy.title')}</h2>
      {p.allergies.length === 0 ? (
        p.noKnownConfirmed
          ? <p role="status" className="inline-flex items-center gap-1.5 font-bold"><CircleCheck aria-hidden className="size-4" />{t('intake.allergy.noneConfirmed')}</p>
          : (
            <div className="flex flex-col items-start gap-2">
              <p className="inline-flex items-center gap-1.5 font-bold"><CircleHelp aria-hidden className="size-4" />{t('intake.allergy.noneRecorded')}</p>
              {!p.readOnly && <Button type="button" variant="outline" className="whitespace-normal" loading={p.confirmingNone} disabled={!p.online} onClick={p.onNone}>{t('intake.allergy.confirmNone')}</Button>}
            </div>
          )
      ) : (
        <ul className="flex flex-col gap-3">
          {p.allergies.map(a => (
            <li key={a.id} className="flex flex-col gap-1.5 rounded-md border p-3">
              <p className="font-bold">{a.substance}{a.reaction ? <span className="font-normal">{` · ${a.reaction}`}</span> : null}</p>
              <p className="text-sm text-muted-foreground">{t(`timeline.source.${a.source}`)}</p>
              {a.status === null && <p className="inline-flex items-center gap-1.5 text-sm font-bold"><CircleHelp aria-hidden className="size-4" />{t('intake.allergy.unset')}</p>}
              <Label htmlFor={`allergy-${a.id}`} className="sr-only">{t('intake.allergy.statusFor', { name: a.substance })}</Label>
              <Select value={a.status ?? ''} disabled={p.readOnly || !p.online || p.settingId === a.id} onValueChange={v => p.onStatus(a.id, v as AllergyStatus)}>
                <SelectTrigger id={`allergy-${a.id}`} className="w-full max-w-[18rem]"><SelectValue placeholder={t('intake.allergy.setStatus')} /></SelectTrigger>
                <SelectContent>{allergyStatuses.map(s => <SelectItem key={s} value={s}>{t(`intake.allergy.status.${s}`)}</SelectItem>)}</SelectContent>
              </Select>
            </li>
          ))}
        </ul>
      )}
      {!p.readOnly && (
        <form noValidate onSubmit={p.onAdd} className="flex flex-col gap-3 border-t pt-3" aria-label={t('intake.allergy.add')}>
          <div className="grid gap-3 sm:grid-cols-2">
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="allergy-substance" className="font-bold">{t('intake.allergy.substance')}</Label>
              <Input id="allergy-substance" autoComplete="off" maxLength={100} aria-invalid={!!errors.substance || !!refused('substance')} {...p.form.register('substance')} />
              {(errors.substance || refused('substance')) && <p role="alert" className={fieldError}>{t(refused('substance') === 'duplicate' ? 'intake.allergy.duplicate' : 'intake.allergy.substanceRequired')}</p>}
            </div>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="allergy-reaction" className="font-bold">{t('intake.allergy.reaction')}</Label>
              <Input id="allergy-reaction" autoComplete="off" maxLength={500} {...p.form.register('reaction')} />
            </div>
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="allergy-new-status" className="font-bold">{t('intake.allergy.newStatus')}</Label>
            <Controller control={p.form.control} name="status" render={({ field }) => (
              <Select value={field.value} onValueChange={field.onChange}>
                <SelectTrigger id="allergy-new-status" className="w-full max-w-[18rem]"><SelectValue /></SelectTrigger>
                <SelectContent>{newAllergyStatuses.map(s => <SelectItem key={s} value={s}>{t(`intake.allergy.status.${s}`)}</SelectItem>)}</SelectContent>
              </Select>
            )} />
          </div>
          <Button type="submit" className="self-start" loading={p.adding} disabled={!p.online}>{t('intake.allergy.add')}</Button>
        </form>
      )}
    </section>
  );
}
