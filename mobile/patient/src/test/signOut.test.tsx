import { fireEvent, screen } from '@testing-library/react-native';
import { useEffect, useRef, useState } from 'react';
import { Pressable, Text } from 'react-native';
import { cast } from '../dev/cast';
import { useSession, useSignedIn } from '../features/session/hooks/session';
import { renderApp } from './render';

// The router keeps a signed-in screen mounted for a render after sign-out; that render must not crash.
function SignedInScreen() {
  const { session, signOut } = useSignedIn();
  return <Pressable accessibilityRole="button" onPress={signOut}><Text>{session.person?.name}</Text></Pressable>;
}
function Harness() {
  const { session, begin } = useSession();
  const [wasIn, setWasIn] = useState(false);
  const began = useRef(false);
  useEffect(() => { if (!began.current) { began.current = true; begin({ person: cast.people[0]!, phone: '+919000000101', clinic: cast.clinic }, false); } }, [begin]); // once, on mount
  if (session && !wasIn) setWasIn(true);
  return <>{session ? null : <Text>signed out</Text>}{wasIn ? <SignedInScreen /> : null}</>;
}

it('signs out without crashing the screens that are still mounted', async () => {
  await renderApp(<Harness />);
  await fireEvent.press(await screen.findByRole('button', { name: 'Meera Iyer' }));
  expect(await screen.findByText('signed out')).toBeOnTheScreen();
});
