import { Link } from 'react-router';
import { ArrowLeft, CircleAlert, CircleCheck, UserPlus } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, buttonVariants, cn, Label, Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@medvya/ui';
import PatientsMessage from '../../patients/components/PatientsMessage';
import { PatientPicker, type PatientSearchView } from '../../schedule/components/BookingPanel';
import { reasonCategories, visitTypes, type PatientMatch } from '../../schedule/domain/booking';
import type { WalkInChoice, WalkInError, WalkInVisit } from '../domain/board';

type Props = {
  patientSearch: PatientSearchView;
  patientId: string;
  patient: { loading: boolean; match: PatientMatch | null } | null;
  doctors: { id: string; name: string }[];
  doctorsLoading: boolean;
  choice: WalkInChoice;
  onChoice: (next: Partial<WalkInChoice>) => void;
  canConfirm: boolean;
  saving: boolean;
  onConfirm: () => void;
  result: WalkInVisit | null;
  error: WalkInError | 'forbidden' | null;
  boardPath: string;
  /** Quick registration, which comes back here with the new patient chosen. */
  registerPath: string;
  timeZone: string | null;
};

const nextAvailable = 'next';

/** P-01 "Walk-in" (MDV-P1-025): patient, doctor (or next available), then one tap: the next free slot today and checked in. */
export default function WalkInPage(p: Props) {
  const { t, formatTime } = useI18n();
  if (p.error === 'forbidden') return <PatientsMessage pageHeading title={t('err.403')} body={t('walkIn.forbidden')} />;
  const back = (
    <Link to={p.boardPath} className={cn(buttonVariants({ variant: 'ghost' }), 'self-start px-2')}>
      <ArrowLeft className="rtl:rotate-180" aria-hidden />{t('walkIn.back')}
    </Link>
  );
  if (p.result) {
    const doctor = p.doctors.find(d => d.id === p.result!.calendarId)?.name ?? '';
    return (
      <div className="flex max-w-[720px] flex-col gap-4">
        {back}
        <div role="status" className="flex flex-col items-start gap-3 rounded-xl border bg-card p-6">
          <CircleCheck className="size-8 text-success-700" aria-hidden />
          <h1 className="text-2xl font-extrabold">{t('walkIn.done.title', { name: p.result.patientName })}</h1>
          <p>{p.result.unscheduled ? t('walkIn.done.queued', { doctor }) : t('walkIn.done.slot', { doctor, time: formatTime(new Date(p.result.start), p.timeZone ?? 'UTC') })}</p>
          <Link to={p.boardPath} className={buttonVariants()}>{t('walkIn.back')}</Link>
        </div>
      </div>
    );
  }

  return (
    <div className="flex max-w-[720px] flex-col gap-5">
      {back}
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-extrabold">{t('walkIn.title')}</h1>
        <p className="text-muted-foreground">{t('walkIn.intro')}</p>
      </div>
      <form noValidate onSubmit={e => { e.preventDefault(); p.onConfirm(); }} className="flex flex-col gap-5 rounded-xl border bg-card p-4">
        <PatientPicker search={p.patientSearch} chosen={p.patientId} registerPath={null} invalid={false} />
        <div className="flex flex-col gap-1">
          {p.patient?.loading && <p className="text-sm text-muted-foreground">{t('patients.loading')}</p>}
          {p.patient?.match && <p className="font-bold" role="status">{t('walkIn.patient', { name: p.patient.match.fullName, mrn: p.patient.match.mrn })}</p>}
          <Link to={p.registerPath} className={cn(buttonVariants({ variant: 'link' }), 'self-start px-0')}><UserPlus aria-hidden />{t('walkIn.register')}</Link>
        </div>

        <div className="grid gap-4 md:grid-cols-3">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="walk-in-doctor">{t('walkIn.doctor')}</Label>
            <Select value={p.choice.calendarId || nextAvailable} onValueChange={v => p.onChoice({ calendarId: v === nextAvailable ? '' : v })} disabled={p.doctorsLoading}>
              <SelectTrigger id="walk-in-doctor" className="min-h-11 w-full"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value={nextAvailable}>{t('walkIn.nextAvailable')}</SelectItem>
                {p.doctors.map(d => <SelectItem key={d.id} value={d.id}>{d.name}</SelectItem>)}
              </SelectContent>
            </Select>
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="walk-in-type">{t('booking.visitType')}</Label>
            <Select value={p.choice.visitType} onValueChange={v => p.onChoice({ visitType: v as WalkInChoice['visitType'] })}>
              <SelectTrigger id="walk-in-type" className="min-h-11 w-full"><SelectValue /></SelectTrigger>
              <SelectContent>{visitTypes.map(v => <SelectItem key={v} value={v}>{t(`visitType.${v}`)}</SelectItem>)}</SelectContent>
            </Select>
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="walk-in-reason">{t('booking.reasonCategory')}</Label>
            <Select value={p.choice.reasonCategory} onValueChange={v => p.onChoice({ reasonCategory: v as WalkInChoice['reasonCategory'] })}>
              <SelectTrigger id="walk-in-reason" className="min-h-11 w-full"><SelectValue /></SelectTrigger>
              <SelectContent>{reasonCategories.map(r => <SelectItem key={r} value={r}>{t(`reasonCategory.${r}`)}</SelectItem>)}</SelectContent>
            </Select>
          </div>
        </div>

        <div aria-live="polite">
          {p.error && (
            <p role="alert" className="flex items-start gap-1.5 text-sm font-semibold text-danger-700">
              <CircleAlert className="mt-0.5 size-4 shrink-0" aria-hidden />{t(`walkIn.error.${p.error}`)}
            </p>
          )}
        </div>
        <Button type="submit" className="self-start" disabled={!p.canConfirm} loading={p.saving}>{t('walkIn.confirm')}</Button>
      </form>
    </div>
  );
}
