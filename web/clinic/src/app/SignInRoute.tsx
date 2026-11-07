import type { ReactNode } from 'react';
import SignInPage from '../features/session/components/SignInPage';
import { useSignInController } from '../features/session/hooks/useSignInController';

export default function SignInRoute({ switcher }: { switcher: ReactNode }) {
  const vm = useSignInController();
  return <SignInPage languageSwitcher={switcher} form={vm.form} failed={vm.failed} onSubmit={e => void vm.submit(e)} onWithoutEmail={vm.startWithoutEmail} />;
}
