import { onlineManager } from '@tanstack/react-query';
import { act, fireEvent, screen, waitFor } from '@testing-library/react-native';
import { useEffect } from 'react';
import { Pressable, Text } from 'react-native';
import { cast } from '../dev/cast';
import { readCopy, saveCopy, storeGeneration } from '../features/api/services/store';
import { LockScreen } from '../features/session/components/LockScreen';
import { useSession } from '../features/session/hooks/session';
import { useLockController } from '../features/session/hooks/useLockController';
import { setPin } from '../features/session/services/session';
import { TodayScreen } from '../features/today/components/TodayScreen';
import { useTodayController } from '../features/today/hooks/useTodayController';
import { files, keychain } from './fakes';
import { renderApp } from './render';

// MDV-P5-034, ADR-011 §3 and §5: the encrypted last copy, its wipe, the PIN fallback and the offline Today.
let mockBiometricWorks = false;
jest.mock('expo-local-authentication', () => ({
  hasHardwareAsync: async () => true, isEnrolledAsync: async () => true, authenticateAsync: async () => ({ success: mockBiometricWorks }),
  supportedAuthenticationTypesAsync: async () => [1], AuthenticationType: { FINGERPRINT: 1, FACIAL_RECOGNITION: 2 },
}));
afterEach(() => { mockBiometricWorks = false; onlineManager.setOnline(true); });

const doctor = { staff: cast.staff[0]!, facility: cast.facilities[0]! };
const noop = () => {};
function Today() {
  return <TodayScreen {...useTodayController()} onOpenAccount={noop} onOpenPatient={noop} onStartConsult={noop} />;
}
function Lock() {
  return <LockScreen {...useLockController()} />;
}
function App({ biometric = false }: { biometric?: boolean }) {
  const { session, locked, begin, lock } = useSession();
  useEffect(() => { begin(doctor, biometric); }, []); // once, on mount
  if (!session) return <Text>signed out</Text>;
  return locked ? <Lock /> : <><Pressable accessibilityRole="button" onPress={lock}><Text>Lock now</Text></Pressable><Today /></>;
}

/** Signs the doctor in, loads Today live (which keeps the encrypted copy), sets a PIN and locks. */
async function signedInThenLocked(biometric = false) {
  await renderApp(<App biometric={biometric} />);
  expect(await screen.findByRole('button', { name: 'Start consultation' })).toBeOnTheScreen();
  await act(() => setPin('246810'));
  await fireEvent.press(screen.getByRole('button', { name: 'Lock now' }));
}

const enterPin = async (pin: string) => {
  await fireEvent.changeText(await screen.findByLabelText('App PIN'), pin);
  await fireEvent.press(screen.getByRole('button', { name: 'Unlock' }));
};

it('leaves nothing of the previous user on the phone after Switch user', async () => {
  await signedInThenLocked();
  expect(files.size).toBe(1); // the encrypted copy
  expect(keychain.size).toBe(2); // its key and the PIN check value
  await fireEvent.press(screen.getByRole('button', { name: 'Switch user' }));
  expect(await screen.findByText('signed out')).toBeOnTheScreen();
  await waitFor(() => { expect(files.size).toBe(0); expect(keychain.size).toBe(0); });
  expect(await readCopy(`today:${doctor.facility.id}`)).toBeNull();
});

it('wipes at launch what a session closed without signing out left behind', async () => {
  await setPin('246810');
  await saveCopy(`today:${doctor.facility.id}`, cast.today, storeGeneration());
  await renderApp(<Text>launched</Text>);
  await waitFor(() => { expect(files.size).toBe(0); expect(keychain.size).toBe(0); });
});

it('unlocks with the right PIN and counts down the wrong ones', async () => {
  await signedInThenLocked();
  await enterPin('111111');
  expect(await screen.findByText('Wrong PIN. Tries left before this phone is cleared: 4')).toBeOnTheScreen();
  await enterPin('246810');
  expect(await screen.findByRole('button', { name: 'Lock now' })).toBeOnTheScreen();
});

it('signs out and wipes the phone after five wrong PINs', async () => {
  await signedInThenLocked();
  for (let i = 0; i < 4; i++) {
    await enterPin('111111');
    expect(await screen.findByText(`Wrong PIN. Tries left before this phone is cleared: ${4 - i}`)).toBeOnTheScreen();
  }
  await enterPin('111111');
  expect(await screen.findByText('signed out')).toBeOnTheScreen();
  await waitFor(() => { expect(files.size).toBe(0); expect(keychain.size).toBe(0); });
});

it('offers the PIN when the fingerprint does not unlock', async () => {
  await signedInThenLocked(true);
  expect(screen.queryByLabelText('App PIN')).toBeNull(); // fingerprint first
  await fireEvent.press(screen.getByRole('button', { name: 'Unlock' }));
  expect(await screen.findByText('Touch ID did not unlock. Enter your app PIN.')).toBeOnTheScreen();
  await enterPin('246810');
  expect(await screen.findByRole('button', { name: 'Lock now' })).toBeOnTheScreen();
});

it('shows the last copy offline with a banner and no Start consultation', async () => {
  await renderApp(<App />);
  expect(await screen.findByRole('button', { name: 'Start consultation' })).toBeOnTheScreen();
  await act(() => onlineManager.setOnline(false));
  expect(await screen.findByText(/^Offline\. This is the copy from \d{1,2}:\d\d\b.*\. It is read-only/)).toBeOnTheScreen();
  expect(screen.getByText('Allergy: Penicillin')).toBeOnTheScreen();
  expect(screen.queryByRole('button', { name: 'Start consultation' })).toBeNull();
  expect(screen.getByRole('button', { name: 'Open record' })).toBeOnTheScreen();
});

it('says so when offline with no copy on the phone', async () => {
  onlineManager.setOnline(false);
  await renderApp(<App />);
  expect(await screen.findByText('You are offline and this phone has no copy from the last 24 hours.')).toBeOnTheScreen();
});
