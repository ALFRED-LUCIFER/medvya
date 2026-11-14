import { screen } from '@testing-library/react-native';
import { cast } from '../dev/cast';
import { ProfileScreen } from '../features/profile/components/ProfileScreen';
import { renderApp } from './render';

// MDV-P5-061: no choice has a control in the app yet, so the screen must not promise "Change any time".
it('says to ask the clinic to change a choice, never "change any time", while no choice has a control', async () => {
  await renderApp(<ProfileScreen session={{ person: cast.people[0]!, phone: '+919000000101', clinic: cast.clinic }} phone="+91 ••••• •0101"
    profile={cast.profile} family={[]} loading={false} error={null} biometric={false} locale="en" version="0.0.0"
    onLock={() => {}} onSignOut={() => {}} />);
  expect(screen.getByText('Appointment reminders')).toBeOnTheScreen();
  expect(screen.getByText(/ask your clinic/)).toBeOnTheScreen();
  expect(screen.queryByText(/change any time/i)).toBeNull();
  expect(screen.queryByRole('switch')).toBeNull();
});
