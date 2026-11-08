import { fireEvent, screen } from '@testing-library/react-native';
import { SignInScreen } from '../features/session/components/SignInScreen';
import { useSession } from '../features/session/hooks/session';
import { useSignInController } from '../features/session/hooks/useSignInController';
import { renderApp } from './render';

jest.mock('expo-local-authentication', () => ({
  hasHardwareAsync: async () => false, isEnrolledAsync: async () => false, authenticateAsync: async () => ({ success: false }),
}));

function Harness() {
  const { session } = useSession();
  const vm = useSignInController();
  const { Text } = jest.requireActual<typeof import('react-native')>('react-native');
  return session ? <Text testID="signed-in">{`${session.person.name}|${session.phone}`}</Text> : <SignInScreen {...vm} />;
}

async function toCode(number: string) {
  await renderApp(<Harness />);
  await fireEvent.press(screen.getByRole('button', { name: 'Skip' }));
  await fireEvent.changeText(screen.getByLabelText('Mobile number'), number);
  await fireEvent.press(screen.getByRole('button', { name: 'Send code' }));
  await fireEvent.changeText(await screen.findByLabelText('6-digit code'), '123456');
  await fireEvent.press(screen.getByRole('button', { name: 'Continue' }));
}

it('walks the onboarding pages before asking for a number', async () => {
  await renderApp(<Harness />);
  expect(screen.getByText('Your clinic, in your pocket')).toBeOnTheScreen();
  await fireEvent.press(screen.getByRole('button', { name: 'Next' }));
  await fireEvent.press(screen.getByRole('button', { name: 'Next' }));
  await fireEvent.press(screen.getByRole('button', { name: 'Continue with mobile number' }));
  expect(screen.getByLabelText('Mobile number')).toBeOnTheScreen();
});

it('signs the patient in as themself; an unlinked family member cannot be chosen', async () => {
  await toCode('90000 00101');
  expect(await screen.findByText('Who are you?')).toBeOnTheScreen();
  expect(screen.queryByRole('button', { name: /Kavya Iyer/ })).toBeNull(); // not linked to this number
  await fireEvent.press(screen.getByRole('button', { name: /Meera Iyer · this is me/ }));
  await fireEvent.press(await screen.findByRole('button', { name: 'Continue' }));
  expect(await screen.findByTestId('signed-in')).toHaveTextContent('Meera Iyer|+919000000101');
});

it('accepts a number with no records until the code, then refuses', async () => {
  await toCode('98765 43210');
  expect(await screen.findByText('That code did not work. Check the app and try again.')).toBeOnTheScreen();
});

it('rejects a number that is not an Indian mobile', async () => {
  await renderApp(<Harness />);
  await fireEvent.press(screen.getByRole('button', { name: 'Skip' }));
  await fireEvent.changeText(screen.getByLabelText('Mobile number'), '12345');
  await fireEvent.press(screen.getByRole('button', { name: 'Send code' }));
  expect(await screen.findByText('Enter a mobile number for the country you picked')).toBeOnTheScreen();
});
