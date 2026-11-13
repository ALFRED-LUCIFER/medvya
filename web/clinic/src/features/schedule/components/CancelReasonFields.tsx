import type { UseFormReturn } from 'react-hook-form';
import { useI18n } from '@medvya/i18n';
import { Label, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Textarea } from '@medvya/ui';
import { cancelReasons, type CancelForm } from '../domain/changes';

/** Why a visit ends early: a reason from the list and a note (required for Other). Shared by the appointment panel and the board's card menu (MDV-P1-100). */
export default function CancelReasonFields({ form, onReason }: { form: UseFormReturn<CancelForm>; onReason: (reason: CancelForm['reason']) => void }) {
  const { t } = useI18n();
  const { register, formState: { errors }, watch } = form;
  const reason = watch('reason');
  const reasonError = errors.reason ? 'appointment.cancel.error.reason' : null;
  const noteError = errors.note?.message === 'noteLong' ? 'appointment.cancel.error.noteLong' : errors.note ? 'appointment.cancel.error.note' : null;
  return (
    <>
      <div className="flex flex-col gap-1.5">
        <Label htmlFor="cancel-reason">{t('appointment.cancel.reason')}</Label>
        <Select value={reason ?? ''} onValueChange={v => onReason(v as CancelForm['reason'])}>
          <SelectTrigger id="cancel-reason" aria-invalid={!!reasonError} aria-describedby={reasonError ? 'cancel-reason-error' : undefined} className="h-11 w-full">
            <SelectValue placeholder={t('appointment.cancel.choose')} />
          </SelectTrigger>
          <SelectContent>{cancelReasons.map(r => <SelectItem key={r} value={r}>{t(`cancelReason.${r}`)}</SelectItem>)}</SelectContent>
        </Select>
        {reasonError && <p id="cancel-reason-error" className="text-xs font-bold text-danger-700">{t(reasonError)}</p>}
      </div>
      <div className="flex flex-col gap-1.5">
        <Label htmlFor="cancel-note">{t(reason === 'Other' ? 'appointment.cancel.noteRequired' : 'appointment.cancel.note')}</Label>
        <Textarea id="cancel-note" rows={3} aria-invalid={!!noteError} aria-describedby={noteError ? 'cancel-note-error cancel-note-hint' : 'cancel-note-hint'} {...register('note')} />
        <p id="cancel-note-hint" className="text-xs text-muted-foreground">{t('appointment.cancel.noteHint')}</p>
        {noteError && <p id="cancel-note-error" className="text-xs font-bold text-danger-700">{t(noteError)}</p>}
      </div>
    </>
  );
}
