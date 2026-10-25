import { useI18n } from '@medvya/i18n';
import { cn, scheduleLooks, type Look } from '@medvya/ui';
import type { Appointment, AppointmentState } from '../domain/schedule';
import { hatched, stateLook } from './looks';

/** State as icon and words (never colour alone): "Booked", "No-show" … */
export function StateLabel({ state }: { state: AppointmentState }) {
  const { t } = useI18n();
  const Icon = stateLook(state).icon;
  return (
    <span className="inline-flex items-center gap-1">
      <Icon aria-hidden className="size-3.5 shrink-0" />
      <strong className="font-extrabold">{t(`schedule.state.${state}`)}</strong>
    </span>
  );
}

/** One appointment as a chip: patient, state, time and visit type, with the state's border (solid, dashed) as the shape cue. */
export function AppointmentChip({ appointment: a, time, className }: { appointment: Appointment; time: (iso: string) => string; className?: string }) {
  const { t } = useI18n();
  const look = stateLook(a.state);
  return (
    <div className={cn('flex flex-col gap-0.5 overflow-hidden rounded-lg px-2 py-1', className)} style={{ backgroundColor: look.bg, color: look.fg, border: look.border }}>
      <span className="truncate text-sm font-bold text-foreground">{a.patientName}</span>
      <span className="flex items-center gap-1 truncate text-xs">
        <StateLabel state={a.state} />
        {t('schedule.appointment.detail', { time: time(a.start), type: t(`visitType.${a.visitType}`) })}
      </span>
    </div>
  );
}

const legend = ['booked', 'checkedIn', 'inRoom', 'completed', 'noShow', 'cancelled', 'held'] as const;

/** What each look means, so states can be told apart by shape and words as well as colour. */
export function Legend() {
  const { t } = useI18n();
  return (
    <ul aria-label={t('schedule.legend')} className="flex flex-wrap gap-x-4 gap-y-1.5 text-xs text-muted-foreground">
      {legend.map(k => {
        const look: Look = scheduleLooks[k], Icon = look.icon;
        return (
          <li key={k} className="flex items-center gap-1.5">
            <span aria-hidden className="size-3.5 rounded-sm" style={{ backgroundColor: look.bg, border: look.border ?? `1px solid ${look.fg}` }} />
            <Icon aria-hidden className="size-3.5" />{t(look.label)}
          </li>
        );
      })}
      <li className="flex items-center gap-1.5">
        <span aria-hidden className={cn('size-3.5 rounded-sm border', hatched)} />
        <scheduleLooks.unavailable.icon aria-hidden className="size-3.5" />{t(scheduleLooks.unavailable.label)}
      </li>
    </ul>
  );
}
