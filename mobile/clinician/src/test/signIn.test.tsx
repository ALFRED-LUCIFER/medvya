import { fireEvent, screen } from '@testing-library/react-native';
import { SignInScreen } from '../features/session/components/SignInScreen';
import { useSession } from '../features/session/hooks/session';
import { useSignInController } from '../features/session/hooks/useSignInController';
import { tabsFor } from '../features/session/domain/session';
import { renderApp } from './render';

jest.mock('expo-local-authentication', () => ({
  hasHardwareAsync: async () => false, isEnrolledAsync: async () => false, authenticateAsync: async () => ({ success: false }),
}));

function Harness() {
  const { session } = useSession();
  const vm = useSignInController();
  return session ? <SignedIn tabs={tabsFor(session.staff.role).join(',')} where={session.facility.name} /> : <SignInScreen {...vm} />;
}
function SignedIn({ tabs, where }: { tabs: string; where: string }) {
  const { Text } = jest.requireActual<typeof import('react-native')>('react-native');
  return <Text testID="signed-in">{`${where}|${tabs}`}</Text>;
}

async function signIn(email: string, code: string) {
  await renderApp(<Harness />);
  await fireEvent.changeText(screen.getByLabelText('Work email'), email);
  await fireEvent.press(screen.getByRole('button', { name: 'Continue' }));
  await fireEvent.changeText(await screen.findByLabelText('6-digit code'), code);
  await fireEvent.press(screen.getByRole('button', { name: 'Sign in' }));
}

it('signs a doctor in through code and facility, with the clinical tabs', async () => {
  await signIn('arjun.rao@example.com', '123456');
  await fireEvent.press(await screen.findByRole('button', { name: /Adyar/ }));
  await fireEvent.press(await screen.findByRole('button', { name: 'Continue' }));
  expect(await screen.findByTestId('signed-in')).toHaveTextContent('Adyar|today,patients,consult,inbox');
});

it('gives front desk no Consult tab', async () => {
  await signIn('reception.desk@example.com', '123456');
  await fireEvent.press(await screen.findByRole('button', { name: /Velachery/ }));
  await fireEvent.press(await screen.findByRole('button', { name: 'Continue' }));
  expect(await screen.findByTestId('signed-in')).toHaveTextContent('Velachery|today,patients,inbox');
});

it('treats an unknown email like a known one until the code, then refuses', async () => {
  await signIn('nobody@example.com', '123456');
  expect(await screen.findByText('That code did not work. Check the app and try again.')).toBeOnTheScreen();
  expect(screen.queryByTestId('signed-in')).toBeNull();
});

it('rejects a malformed email and code before calling anything', async () => {
  await renderApp(<Harness />);
  await fireEvent.changeText(screen.getByLabelText('Work email'), 'arjun');
  await fireEvent.press(screen.getByRole('button', { name: 'Continue' }));
  expect(await screen.findByText('Enter an email like name@clinic.com')).toBeOnTheScreen();
});
