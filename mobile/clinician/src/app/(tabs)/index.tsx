import { router } from 'expo-router';
import { TodayScreen } from '../../features/today/components/TodayScreen';
import { useTodayController } from '../../features/today/hooks/useTodayController';

export default function TodayRoute() {
  return <TodayScreen {...useTodayController()} onOpenAccount={() => router.push('/account')}
    onOpenPatient={id => router.push(`/patients/${id}`)} onStartConsult={() => router.navigate('/consult')} />;
}
