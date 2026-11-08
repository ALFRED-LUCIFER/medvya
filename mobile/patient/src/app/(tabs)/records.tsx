import { RecordsScreen } from '../../features/records/components/RecordsScreen';
import { useRecordsController } from '../../features/records/hooks/useRecordsController';

export default function RecordsRoute() {
  return <RecordsScreen {...useRecordsController()} />;
}
