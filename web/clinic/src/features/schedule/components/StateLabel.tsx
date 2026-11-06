import { useI18n } from '@medvya/i18n';
import type { AppointmentState } from '../domain/schedule';
import { stateLook } from './looks';

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
