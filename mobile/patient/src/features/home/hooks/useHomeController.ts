import { useI18n } from '@medvya/i18n';
import { useQuery } from '@tanstack/react-query';
import { errorKey } from '../../api/domain/errors';
import { useSignedIn } from '../../session/hooks/session';
import { emergencyNumber } from '../domain/home';
import { call, fetchHome, openDirections } from '../services/home';

export function useHomeController() {
  const { session } = useSignedIn();
  const { country } = useI18n();
  const query = useQuery({ queryKey: ['home', session.person?.id], queryFn: () => fetchHome(session.person?.id) });
  const emergency = emergencyNumber(session.clinic?.country ?? country);
  return {
    session,
    home: query.data ?? null,
    loading: query.isPending,
    error: query.error ? errorKey(query.error) : null,
    refreshing: query.isRefetching,
    onRefresh: () => void query.refetch(),
    emergency,
    onCallEmergency: () => void call(emergency),
    onCallClinic: () => { if (session.clinic) void call(session.clinic.phone); },
    onDirections: () => { if (session.clinic) void openDirections(session.clinic.address); },
  };
}
