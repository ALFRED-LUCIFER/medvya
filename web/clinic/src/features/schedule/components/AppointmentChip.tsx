import { useI18n } from '@medvya/i18n';
import { cn } from '@medvya/ui';
import type { Appointment } from '../domain/schedule';
import { stateLook } from './looks';
import { StateLabel } from './StateLabel';

/**
 * One appointment as a chip: patient, state, time and visit type, with the state's border (solid, dashed) as the shape cue.
 * In the day grid its height is the visit's length, so lines truncate; in the week and list they wrap.
 */
export function AppointmentChip({ appointment: a, time, wrap = false, className }: { appointment: Appointment; time: (iso: string) => string; wrap?: boolean; className?: string }) {
  const { t } = useI18n();
  const look = stateLook(a.state);
  return (
    // A span (displayed as a flex box) so the chip is valid inside the button that opens it.
    <span className={cn('flex flex-col gap-0.5 overflow-hidden rounded-lg px-2 py-1', className)} style={{ backgroundColor: look.bg, color: look.fg, border: look.border }}>
      <span className={cn('text-sm font-bold text-foreground', wrap ? 'break-words' : 'truncate')}>{a.patientName}</span>
      <span className={cn('flex items-center gap-x-1 text-xs', wrap ? 'flex-wrap' : 'truncate')}>
        <StateLabel state={a.state} />
        {t('schedule.appointment.detail', { time: time(a.start), type: t(`visitType.${a.visitType}`) })}
      </span>
    </span>
  );
}
