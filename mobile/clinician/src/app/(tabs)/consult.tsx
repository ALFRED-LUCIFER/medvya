import { router } from 'expo-router';
import { ConsultScreen } from '../../features/consult/components/ConsultScreen';
import { useSignedIn } from '../../features/session/hooks/session';

export default function ConsultRoute() {
  const { session } = useSignedIn();
  return <ConsultScreen facility={session.facility.name} staff={session.staff.name} onOpenAccount={() => router.push('/account')} />;
}
