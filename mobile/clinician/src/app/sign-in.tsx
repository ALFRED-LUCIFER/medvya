import { SignInScreen } from '../features/session/components/SignInScreen';
import { useSignInController } from '../features/session/hooks/useSignInController';

export default function SignInRoute() {
  return <SignInScreen {...useSignInController()} />;
}
