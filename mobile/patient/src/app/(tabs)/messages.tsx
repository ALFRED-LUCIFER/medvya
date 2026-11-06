import { MessagesScreen } from '../../features/messages/components/MessagesScreen';
import { useMessagesController } from '../../features/messages/hooks/useMessagesController';

export default function MessagesRoute() {
  return <MessagesScreen {...useMessagesController()} />;
}
