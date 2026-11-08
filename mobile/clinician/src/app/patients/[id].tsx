import { router, useLocalSearchParams } from 'expo-router';
import { PatientRecordScreen } from '../../features/patients/components/PatientRecordScreen';
import { usePatientRecordController } from '../../features/patients/hooks/usePatientRecordController';

export default function PatientRoute() {
  const { id } = useLocalSearchParams<{ id: string }>();
  return <PatientRecordScreen {...usePatientRecordController(id)} onBack={() => router.back()} />;
}
