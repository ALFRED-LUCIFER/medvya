import type { ReactNode } from 'react';
import { useParams } from 'react-router';
import InvitationPage from '../features/invitations/components/InvitationPage';
import { useInvitationController } from '../features/invitations/hooks/useInvitationController';

export default function InvitationRoute({ switcher }: { switcher: ReactNode }) {
  const vm = useInvitationController(useParams().token ?? '');
  return <InvitationPage {...vm} onJoin={vm.join} onRetry={vm.retry} languageSwitcher={switcher} />;
}
