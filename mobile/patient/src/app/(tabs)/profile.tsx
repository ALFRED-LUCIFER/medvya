import { ProfileScreen } from '../../features/profile/components/ProfileScreen';
import { useProfileController } from '../../features/profile/hooks/useProfileController';

export default function ProfileRoute() {
  return <ProfileScreen {...useProfileController()} />;
}
