import { Link } from 'react-router';
import { TriangleAlert, UserCheck } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, buttonVariants, Card, PageContainer, PageHeader } from '@medvya/ui';
import type { Missing } from '../../patients/domain/directory';
import type { RegistrationResult } from '../domain/registration';

type Props = {
  result: RegistrationResult; name: string; actorName: string; onRegisterAnother: () => void;
  recordPath: string; bookPath: string | null; checkInPath: string | null;
  /** Opened from the booking panel: the held slot's address, to go back to it with this patient chosen (MDV-P1-021). */
  slot?: string | null;
  quick: boolean; missing: Missing[];
  /** The facility's IANA zone (session.active.timeZone); the browser's zone only when the server sent none. */
  timeZone?: string;
};

/** "{name} is registered" with MRN, time and actor, then the next desk actions (P-02 success). */
export default function RegistrationSuccess({ result, name, actorName, onRegisterAnother, recordPath, bookPath, slot, checkInPath, quick, missing, timeZone }: Props) {
  const { t, formatDate, formatTime, locale } = useI18n();
  const at = new Date(result.registeredAt);
  return (
    // The whole result is the live region, so "… is registered" is announced with its MRN, as before the title moved into PageHeader.
    <PageContainer width="reading" role="status">
      <PageHeader title={<span className="inline-flex items-center gap-2"><UserCheck className="size-6 shrink-0 text-success-700" aria-hidden />{t('registration.done.title', { name })}</span>} />
      <Card>
        <p className="text-muted-foreground">
          {t('registration.done.meta', { mrn: result.mrn, date: formatDate(at, timeZone), time: formatTime(at, timeZone), actor: actorName })}
        </p>
        {result.warnings.map(w => (
          <p key={w} className="flex items-start gap-2 rounded-md border px-3 py-2 text-sm text-warning-700">
            <TriangleAlert className="mt-0.5 size-4 shrink-0" aria-hidden />{t(`registration.done.warning.${w === 'emirates_id.expired' ? w : 'generic'}`)}
          </p>
        ))}
        {result.status === 'Incomplete' && (
          <p className="flex items-start gap-2 rounded-md border px-3 py-2 text-sm text-warning-700">
            <TriangleAlert className="mt-0.5 size-4 shrink-0" aria-hidden />
            <span><strong>{t(quick ? 'registration.done.quickTitle' : 'registration.done.incompleteTitle')}</strong>{' '}
              {t('registration.done.incompleteBody', { items: new Intl.ListFormat([locale], { type: 'conjunction' }).format(missing.map(m => t(`patients.completion.item.${m}`))) })}</span>
          </p>
        )}
        {/* Quick mode asks no reminder consent: say where it is added, so reminders are not silently never sent (MDV-P1-052). */}
        {quick && <p className="text-sm">{t('registration.done.quickConsents')}</p>}
        <div className="flex flex-wrap gap-2">
          {bookPath && (slot
            ? <Link to={`${bookPath}?${slot}&${new URLSearchParams({ patient: result.patientId })}`} className={buttonVariants()}>{t('registration.done.backToBooking')}</Link>
            : <Link to={`${bookPath}?${new URLSearchParams({ patient: result.patientId })}`} className={buttonVariants()}>{t('registration.done.book')}</Link>)}
          {/* Walk-in with this patient chosen: the id is opaque, never patient data. */}
          {checkInPath && <Link to={`${checkInPath}?${new URLSearchParams({ patient: result.patientId })}`} className={buttonVariants({ variant: 'outline' })}>{t('registration.done.checkIn')}</Link>}
          <Link to={recordPath} className={buttonVariants({ variant: 'outline' })}>{t('registration.done.open')}</Link>
          <Button variant="ghost" onClick={onRegisterAnother}>{t('registration.done.another')}</Button>
        </div>
      </Card>
    </PageContainer>
  );
}
