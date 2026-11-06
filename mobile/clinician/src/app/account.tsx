import { router } from 'expo-router';
import { AccountScreen } from '../features/account/components/AccountScreen';
import { useAccountController } from '../features/account/hooks/useAccountController';

export default function AccountRoute() {
  return <AccountScreen {...useAccountController()} onBack={() => router.back()} />;
}
