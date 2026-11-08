import { router } from 'expo-router';
import { InboxScreen } from '../../features/inbox/components/InboxScreen';
import { useInboxController } from '../../features/inbox/hooks/useInboxController';

export default function InboxRoute() {
  return <InboxScreen {...useInboxController()} onOpenAccount={() => router.push('/account')} />;
}
