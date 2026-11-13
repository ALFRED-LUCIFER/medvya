import { useRef } from 'react';
import { Link } from 'react-router';
import { CalendarClock } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, buttonVariants, Dialog, DialogContent, DialogDescription, DialogTitle, Label, Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@medvya/ui';
import { dismissReasons, type DismissReason, type FollowUpDue } from '../domain/followUpDue';

type DialogProps = {
  dismissing: { followUp: FollowUpDue; name: string } | null; reason: DismissReason | null; reasonMissing: boolean; working: boolean; failed: boolean; online: boolean;
  onReason: (reason: DismissReason) => void; onConfirm: () => void; onCancel: () => void;
};

type LineProps = {
  followUp: FollowUpDue;
  patientName: string;
  /** The schedule on the due day with the patient and doctor chosen; null when this role may not book (the line is then words only). */
  bookPath: string | null;
  online: boolean;
  onDismiss: ((followUp: FollowUpDue, name: string) => void) | null;
};

/** "Follow-up due from <day>" with Book and Dismiss (MDV-P1-102): the date and the doctor only, never why. Words and an icon, never colour alone. */
export function FollowUpDueLine({ followUp: f, patientName, bookPath, online, onDismiss }: LineProps) {
  const { t, formatDay } = useI18n();
  const id = `follow-up-${f.id}`;
  return (
    <div role="group" aria-labelledby={id} className="flex flex-col gap-1.5 rounded-md border border-info-border bg-info-bg p-2">
      <p id={id} className="inline-flex items-start gap-1.5 text-label text-info-text">
        <CalendarClock aria-hidden className="mt-0.5 size-4 shrink-0" />
        {t(f.calendarName ? 'followUpDue.withDoctor' : 'followUpDue.line', { day: formatDay(f.dueOn), doctor: f.calendarName ?? '' })}
      </p>
      <div className="flex flex-wrap gap-1.5">
        {bookPath && <Link to={bookPath} className={buttonVariants({ variant: 'outline', className: 'whitespace-normal' })}
          aria-label={t('followUpDue.bookFor', { name: patientName })}>{t('followUpDue.book')}</Link>}
        {onDismiss && <Button id={`${id}-dismiss`} variant="ghost" className="whitespace-normal" disabled={!online}
          aria-label={t('followUpDue.dismissFor', { name: patientName })} onClick={() => onDismiss(f, patientName)}>{t('followUpDue.dismiss')}</Button>}
      </div>
    </div>
  );
}

/** The reason to take the follow-up off the list, then confirm; nothing changes until then. Focus goes back to the Dismiss button, or the page. */
export function FollowUpDismissDialog({ dismissing, reason, reasonMissing, working, failed, online, onReason, onConfirm, onCancel }: DialogProps) {
  const { t, formatDay } = useI18n();
  const opener = useRef<string | null>(null);
  return (
    <Dialog open={dismissing !== null} onOpenChange={open => { if (!open) onCancel(); }}>
      <DialogContent closeLabel={t('followUpDue.dismiss.close')} onOpenAutoFocus={() => { opener.current = dismissing?.followUp.id ?? null; }}
        onCloseAutoFocus={e => { e.preventDefault(); (document.getElementById(`follow-up-${opener.current}-dismiss`) ?? document.querySelector('main'))?.focus(); }}>
        <DialogTitle>{t('followUpDue.dismiss.title', { name: dismissing?.name ?? '' })}</DialogTitle>
        <form noValidate className="flex flex-col gap-3" onSubmit={e => { e.preventDefault(); onConfirm(); }}>
          <DialogDescription>{dismissing && t('followUpDue.dismiss.body', { day: formatDay(dismissing.followUp.dueOn) })}</DialogDescription>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="follow-up-dismiss-reason">{t('followUpDue.dismiss.reason')}</Label>
            <Select value={reason ?? ''} onValueChange={v => onReason(v as DismissReason)}>
              <SelectTrigger id="follow-up-dismiss-reason" className="w-full" aria-invalid={reasonMissing} aria-describedby={reasonMissing ? 'follow-up-dismiss-error' : undefined}>
                <SelectValue placeholder={t('followUpDue.dismiss.choose')} />
              </SelectTrigger>
              <SelectContent>{dismissReasons.map(r => <SelectItem key={r} value={r}>{t(`followUpDue.reason.${r}`)}</SelectItem>)}</SelectContent>
            </Select>
            {reasonMissing && <p id="follow-up-dismiss-error" className="text-caption text-danger-text">{t('followUpDue.dismiss.required')}</p>}
          </div>
          {failed && <p role="alert" className="text-label text-danger-text">{t('followUpDue.dismiss.failed')}</p>}
          <div className="flex flex-wrap gap-2">
            <Button type="submit" loading={working} disabled={!online}>{t('followUpDue.dismiss.confirm')}</Button>
            <Button type="button" variant="outline" disabled={working} onClick={onCancel}>{t('appointment.back')}</Button>
          </div>
        </form>
      </DialogContent>
    </Dialog>
  );
}
