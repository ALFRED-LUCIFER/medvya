import { fireEvent, screen } from '@testing-library/react-native';
import { useEffect, useRef } from 'react';
import { Pressable, Text } from 'react-native';
import { cast } from '../dev/cast';
import { useSession, useSignedIn } from '../features/session/hooks/session';
import { renderApp } from './render';

// The router keeps a signed-in screen mounted for a render after sign-out; that render must not crash.
function SignedInScreen() {
  const { session, signOut } = useSignedIn();
  return <Pressable accessibilityRole="button" onPress={signOut}><Text>{session.staff.name}</Text></Pressable>;
}
function Harness() {
  const { session, begin } = useSession();
  const wasIn = useRef(false);
  useEffect(() => { begin({ staff: cast.staff[0]!, facility: cast.facilities[0]! }, false); }, []); // once, on mount
  if (session) wasIn.current = true;
  return <>{session ? null : <Text>signed out</Text>}{wasIn.current ? <SignedInScreen /> : null}</>;
}

it('signs out without crashing the screens that are still mounted', async () => {
  await renderApp(<Harness />);
  await fireEvent.press(await screen.findByRole('button', { name: 'Dr. Arjun Rao' }));
  expect(await screen.findByText('signed out')).toBeOnTheScreen();
});
