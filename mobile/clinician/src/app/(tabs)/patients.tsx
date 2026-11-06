import { router } from 'expo-router';
import { PatientsScreen } from '../../features/patients/components/PatientsScreen';
import { usePatientsController } from '../../features/patients/hooks/usePatientsController';

export default function PatientsRoute() {
  return <PatientsScreen {...usePatientsController()} onOpenAccount={() => router.push('/account')} onOpenPatient={id => router.push(`/patients/${id}`)} />;
}
