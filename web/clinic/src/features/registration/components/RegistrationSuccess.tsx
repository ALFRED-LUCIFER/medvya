import { Link } from 'react-router';
import { TriangleAlert, UserCheck } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, buttonVariants } from '@medvya/ui';
import type { RegistrationResult } from '../domain/registration';

type Props = {
  result: RegistrationResult; name: string; actorName: string; onRegisterAnother: () => void;
  recordPath: string; bookPath: string | null; checkInPath: string | null;
};

/** "{name} is registered" with MRN, time and actor, then the next desk actions (P-02 success). */
export default function RegistrationSuccess({ result, name, actorName, onRegisterAnother, recordPath, bookPath, checkInPath }: Props) {
  const { t, formatDate, locale, country } = useI18n();
  const at = new Date(result.registeredAt);
  const time = new Intl.DateTimeFormat(`${locale}-${country}-u-nu-latn`, { hour: '2-digit', minute: '2-digit' }).format(at);
  return (
    <div role="status" className="flex max-w-[880px] flex-col gap-4 rounded-xl border bg-card p-6">
      <UserCheck className="size-8 text-success-700" aria-hidden />
      <h1 className="text-2xl font-extrabold">{t('registration.done.title', { name })}</h1>
      <p className="text-muted-foreground">
        {t('registration.done.meta', { mrn: result.mrn, date: formatDate(at), time, actor: actorName })}
      </p>
      {result.warnings.map(w => (
        <p key={w} className="flex items-start gap-2 rounded-md border px-3 py-2 text-sm text-warning-700">
          <TriangleAlert className="mt-0.5 size-4 shrink-0" aria-hidden />{t(`registration.done.warning.${w === 'emirates_id.expired' ? w : 'generic'}`)}
        </p>
      ))}
      {result.status === 'Incomplete' && (
        <p className="flex items-start gap-2 rounded-md border px-3 py-2 text-sm text-warning-700">
          <TriangleAlert className="mt-0.5 size-4 shrink-0" aria-hidden />
          <span><strong>{t('registration.done.quickTitle')}</strong> {t('registration.done.quickBody')}</span>
        </p>
      )}
      <div className="flex flex-wrap gap-2">
        {bookPath && <Link to={bookPath} className={buttonVariants()}>{t('registration.done.book')}</Link>}
        {checkInPath && <Link to={checkInPath} className={buttonVariants({ variant: 'outline' })}>{t('registration.done.checkIn')}</Link>}
        <Link to={recordPath} className={buttonVariants({ variant: 'outline' })}>{t('registration.done.open')}</Link>
        <Button variant="ghost" onClick={onRegisterAnother}>{t('registration.done.another')}</Button>
      </div>
    </div>
  );
}
