import InviteColleagueForm from '../features/invitations/components/InviteColleagueForm';
import { useInviteColleagueController } from '../features/invitations/hooks/useInviteColleagueController';

export default function InviteColleagueRoute() {
  return <InviteColleagueForm {...useInviteColleagueController()} />;
}
