import { Link } from 'react-router';
import { ArrowLeft, CircleAlert, CircleCheck, UserPlus } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, buttonVariants, Card, cn, Label, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, PageContainer, PageHeader, PageMessage } from '@medvya/ui';
import { PatientPicker, type PatientSearchView } from '../../schedule/components/BookingPanel';
import { reasonCategories, visitTypes, type PatientMatch } from '../../schedule/domain/booking';
import { termsPath } from '../../settings/domain/settings';
import { isBoardColumn, type BoardCard, type WalkInChoice, type WalkInError, type WalkInVisit } from '../domain/board';

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
  onConfirmRepeat: () => void;
  onRegister: () => void;
  result: (WalkInVisit & Pick<BoardCard, 'token' | 'state'>) | null;
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
  const back = (
    <Link to={p.boardPath} className={cn(buttonVariants({ variant: 'ghost' }), 'px-2')}>
      <ArrowLeft className="rtl:rotate-180" aria-hidden />{t('walkIn.back')}
    </Link>
  );
  if (p.error === 'forbidden')
    return <PageContainer width="form"><PageHeader back={back} title={t('walkIn.title')} /><PageMessage kind="denied" title={t('err.403')}>{t('walkIn.forbidden')}</PageMessage></PageContainer>;
  if (p.result) {
    const doctor = p.doctors.find(d => d.id === p.result!.calendarId)?.name ?? '';
    return (
      // The whole result is the live region, so "… is checked in" is announced with its token, as before the title moved into PageHeader.
      <PageContainer width="form" role="status">
        <PageHeader back={back} title={<span className="inline-flex items-center gap-2"><CircleCheck className="size-6 shrink-0 text-success-700" aria-hidden />{t('walkIn.done.title', { name: p.result.patientName })}</span>} />
        <Card className="items-start">
          {/* The token the waiting room is called by: read out to the patient now, not looked up on the board later. */}
          {p.result.token && <p className="text-3xl font-extrabold">{t('walkIn.done.token')} <bdi dir="ltr" className="font-mono">{p.result.token}</bdi></p>}
          <p>
            {p.result.unscheduled ? t('walkIn.done.queued', { doctor }) : t('walkIn.done.slot', { doctor, time: formatTime(new Date(p.result.start), p.timeZone ?? 'UTC') })}
            {isBoardColumn(p.result.state) && <> {t('walkIn.done.column', { column: t(`board.column.${p.result.state}`) })}</>}
          </p>
          <Link to={p.boardPath} className={buttonVariants()}>{t('walkIn.back')}</Link>
        </Card>
      </PageContainer>
    );
  }

  return (
    <PageContainer width="form" className="gap-5">
      <PageHeader back={back} title={t('walkIn.title')} description={t('walkIn.intro')} />
      <Card>
        <form noValidate onSubmit={e => { e.preventDefault(); p.onConfirm(); }} className="flex flex-col gap-5">
          <PatientPicker search={p.patientSearch} chosen={p.patientId} registerPath={null} invalid={false} />
          <div className="flex flex-col gap-1">
            {p.patient?.loading && <p className="text-sm text-muted-foreground">{t('patients.loading')}</p>}
            {p.patient?.match && <p className="font-bold" role="status">{t('walkIn.patient', { name: p.patient.match.fullName, mrn: p.patient.match.mrn })}</p>}
            <Link to={p.registerPath} onClick={p.onRegister} className={cn(buttonVariants({ variant: 'link' }), 'self-start px-0')}><UserPlus aria-hidden />{t('walkIn.register')}</Link>
          </div>

          <div className="grid gap-4 md:grid-cols-3">
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="walk-in-doctor">{t('walkIn.doctor')}</Label>
              <Select value={p.choice.calendarId || nextAvailable} onValueChange={v => p.onChoice({ calendarId: v === nextAvailable ? '' : v })} disabled={p.doctorsLoading}>
                <SelectTrigger id="walk-in-doctor" className="w-full"><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value={nextAvailable}>{t('walkIn.nextAvailable')}</SelectItem>
                  {p.doctors.map(d => <SelectItem key={d.id} value={d.id}>{d.name}</SelectItem>)}
                </SelectContent>
              </Select>
            </div>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="walk-in-type">{t('booking.visitType')}</Label>
              <Select value={p.choice.visitType} onValueChange={v => p.onChoice({ visitType: v as WalkInChoice['visitType'] })}>
                <SelectTrigger id="walk-in-type" className="w-full"><SelectValue /></SelectTrigger>
                <SelectContent>{visitTypes.map(v => <SelectItem key={v} value={v}>{t(`visitType.${v}`)}</SelectItem>)}</SelectContent>
              </Select>
            </div>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="walk-in-reason">{t('booking.reasonCategory')}</Label>
              <Select value={p.choice.reasonCategory} onValueChange={v => p.onChoice({ reasonCategory: v as WalkInChoice['reasonCategory'] })}>
                <SelectTrigger id="walk-in-reason" className="w-full"><SelectValue /></SelectTrigger>
                <SelectContent>{reasonCategories.map(r => <SelectItem key={r} value={r}>{t(`reasonCategory.${r}`)}</SelectItem>)}</SelectContent>
              </Select>
            </div>
          </div>

          <div aria-live="polite">
            {p.error && (
              <p role="alert" className="flex items-start gap-1.5 text-sm font-semibold text-danger-700">
                <CircleAlert className="mt-0.5 size-4 shrink-0" aria-hidden />{t(`walkIn.error.${p.error}`)}
                {p.error === 'terms_not_accepted' && <Link to={termsPath} className={cn(buttonVariants({ variant: 'link' }), 'h-auto px-0')}>{t('terms.open')}</Link>}
              </p>
            )}
            {p.error === 'already_today' && (
              <div className="mt-3 flex flex-wrap gap-3">
                <Link to={p.boardPath} className={buttonVariants({ variant: 'outline' })}>{t('walkIn.back')}</Link>
                <Button type="button" variant="outline" loading={p.saving} onClick={p.onConfirmRepeat}>{t('walkIn.repeat')}</Button>
              </div>
            )}
          </div>
          {/* Disabled until a patient is chosen, and says so in words beside it (MDV-P2-058). */}
          <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
            <Button type="submit" disabled={!p.canConfirm} loading={p.saving} aria-describedby={p.patientId ? undefined : 'walk-in-confirm-reason'}>{t('walkIn.confirm')}</Button>
            {!p.patientId && <p id="walk-in-confirm-reason" className="text-body text-muted-foreground">{t('walkIn.confirmReason')}</p>}
          </div>
        </form>
      </Card>
    </PageContainer>
  );
}
