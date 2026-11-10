import { CalendarPlus, CircleCheck, FileUp, FolderInput, Plug, Store, Users, type LucideIcon } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button } from '@medvya/ui';
import { specialtyIdOf, type FinishResult, type WizardForm } from '../domain/onboarding';

type Props = { form: WizardForm; done: FinishResult; homeLabel: 'today' | 'frontDesk'; onBook: () => void; onHome: () => void };

/**
 * The ready screen after finishing (6 Nov design): a summary of what was set up, then book the first patient or open the app.
 * shortcut: no "try a sample day" with 3 synthetic patients; patients cannot be flagged as samples or deleted together yet, and
 * mixing them with real patients is unsafe. Add it with a sample flag and a bulk delete in the Patients module.
 */
export default function ReadyScreen({ form, done, homeLabel, onBook, onHome }: Props) {
  const { t } = useI18n();
  const specialties = form.specialties.map(specialtyIdOf).filter(id => id !== undefined).slice(0, 2).map(id => t(`onboarding.specialty.${id}`)).join(', ');
  const rows: { icon: LucideIcon; label: string; value: string }[] = [
    { icon: Store, label: t(`onboarding.mode.${form.mode}`), value: specialties || form.specialty },
    { icon: CalendarPlus, label: t('onboarding.ready.open', { n: form.days.length }), value: t('onboarding.slotOption', { n: form.slotMinutes }) },
    { icon: Users, label: t('onboarding.ready.team', { n: done.invitationsSent }), value: done.invitationsSent > 0 ? t('onboarding.ready.teamEmail') : '' },
    { icon: FolderInput, label: t(`onboarding.ready.patients.${form.patients || 'none'}`), value: '' },
    { icon: Plug, label: t('onboarding.ready.connections'), value: '' },
  ];
  return (
    <div className="flex flex-col gap-6">
      <div role="status" className="flex flex-col items-start gap-3">
        <CircleCheck className="size-12 text-success-700" aria-hidden />
        <h1 className="text-2xl font-extrabold">{t('onboarding.ready.title', { name: form.clinicName.trim() })}</h1>
        <p className="text-muted-foreground">{t('onboarding.ready.body')}</p>
        {done.invitationsFailed > 0 && <p className="font-semibold text-warning-700">{t('onboarding.done.invitesFailed', { n: done.invitationsFailed })}</p>}
      </div>
      <dl className="flex flex-col divide-y rounded-xl border">
        {rows.map(({ icon: Icon, label, value }) => (
          <div key={label} className="flex min-h-12 items-center gap-3 px-4 py-2 text-sm">
            <Icon className="size-5 shrink-0 text-brand-700" aria-hidden />
            <dt className="flex-1 font-semibold">{label}</dt>
            <dd className="text-muted-foreground">{value}</dd>
          </div>
        ))}
      </dl>
      <div className="flex flex-wrap gap-3">
        <Button onClick={onBook}><FileUp className="size-4" aria-hidden />{t('onboarding.ready.book')}</Button>
        <Button variant="outline" onClick={onHome}>{t(`onboarding.finish.${homeLabel}`)}</Button>
      </div>
    </div>
  );
}
