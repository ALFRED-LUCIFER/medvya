import type { ReactNode } from 'react';
import InvitationPage from '../features/invitations/components/InvitationPage';
import { useInvitationController } from '../features/invitations/hooks/useInvitationController';

export default function InvitationRoute({ switcher }: { switcher: ReactNode }) {
  const vm = useInvitationController();
  // The invitee has no facility yet: the expiry shows in their own device's zone, said explicitly.
  return <InvitationPage {...vm} timeZone={Intl.DateTimeFormat().resolvedOptions().timeZone} onJoin={vm.join} onRetry={vm.retry} languageSwitcher={switcher} />;
}
