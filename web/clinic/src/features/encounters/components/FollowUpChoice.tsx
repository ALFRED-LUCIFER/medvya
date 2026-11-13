import { CalendarCheck, CalendarClock } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button } from '@medvya/ui';
import { followUpPeriods, type FollowUp, type FollowUpPeriod, type FollowUpProblem } from '../domain/followUp';

type Props = {
  followUp: FollowUp | null; loading: boolean; failed: boolean; ended: boolean; saving: boolean; problem: FollowUpProblem | null;
  onPick: (period: FollowUpPeriod) => void;
};

/**
 * Follow-up (MDV-P2-043, design `fu`): one choice, which the front desk sees on its waitlist as a booking to make for this patient, from the due
 * day. Picking the chosen one again takes it off. Where the desk is with it is words and an icon, never colour alone.
 */
export default function FollowUpChoice(p: Props) {
  const { t, formatDay } = useI18n();
  if (p.loading) return <p role="status" className="text-sm">{t('plan.followUp.loading')}</p>;
  if (p.failed || !p.followUp) return <p role="alert" className="text-sm font-bold text-danger-700">{t('plan.followUp.loadFailed')}</p>;
  const f = p.followUp;
  const booked = f.state === 'Booked';
  return (
    <div className="flex flex-col gap-2">
      <div role="group" aria-labelledby="plan-follow-up" className="flex flex-wrap items-center gap-2">
        <span id="plan-follow-up" className="font-bold">{t('plan.followUp.title')}</span>
        {followUpPeriods.map(period => (
          <Button key={period} type="button" size="sm" variant={f.period === period ? 'selected' : 'outline'} aria-pressed={f.period === period}
            disabled={p.saving || p.ended || booked} onClick={() => p.onPick(period)}>{t(`plan.followUp.${period}`)}</Button>
        ))}
      </div>
      <p role="status" className="inline-flex items-start gap-1.5 text-sm">
        {booked ? <CalendarCheck aria-hidden className="mt-0.5 size-4 shrink-0" /> : <CalendarClock aria-hidden className="mt-0.5 size-4 shrink-0" />}
        {f.dueOn ? t(booked ? 'plan.followUp.booked' : 'plan.followUp.toBook', { day: formatDay(f.dueOn) }) : t('plan.followUp.none')}
      </p>
      {p.problem && <p role="alert" className="text-sm font-bold text-danger-700">{t(`plan.followUp.error.${p.problem}`)}</p>}
    </div>
  );
}
