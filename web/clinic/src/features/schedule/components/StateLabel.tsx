import { useI18n } from '@medvya/i18n';
import { Chip } from '@medvya/ui';
import type { AppointmentState } from '../domain/schedule';
import { stateLook, stateTone } from './looks';

/** State as the library Chip (MDV-P2-060): icon and words, never colour alone ("Booked", "No-show" …). */
export function StateLabel({ state }: { state: AppointmentState }) {
  const { t } = useI18n();
  return <Chip tone={stateTone(state)} size="sm" icon={stateLook(state).icon} label={t(`schedule.state.${state}`)} />;
}
