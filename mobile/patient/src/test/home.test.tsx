import { fireEvent, screen } from '@testing-library/react-native';
import { cast } from '../dev/cast';
import { HomeScreen } from '../features/home/components/HomeScreen';
import { renderApp } from './render';

it('shows the next visit in the clinic time zone and a tappable emergency line', async () => {
  const onCallEmergency = jest.fn();
  await renderApp(<HomeScreen session={{ person: cast.people[0]!, phone: '+919000000101', clinic: cast.clinic }} home={cast.home}
    loading={false} error={null} refreshing={false} onRefresh={() => {}} emergency="108" onCallEmergency={onCallEmergency}
    onCallClinic={() => {}} onDirections={() => {}} />);
  expect(screen.getByText('02 Jan 2027 · 09:20')).toBeOnTheScreen(); // 03:50 UTC is 09:20 in Chennai
  expect(screen.getByText('Pay ₹600.00 for your visit')).toBeOnTheScreen();
  await fireEvent.press(screen.getByRole('button', { name: 'For an emergency, call 108' }));
  expect(onCallEmergency).toHaveBeenCalled();
});
