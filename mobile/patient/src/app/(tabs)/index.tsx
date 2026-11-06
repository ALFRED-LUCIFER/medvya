import { HomeScreen } from '../../features/home/components/HomeScreen';
import { useHomeController } from '../../features/home/hooks/useHomeController';

export default function HomeRoute() {
  return <HomeScreen {...useHomeController()} />;
}
