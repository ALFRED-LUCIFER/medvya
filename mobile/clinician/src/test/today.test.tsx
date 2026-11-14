import { screen } from '@testing-library/react-native';
import { cast } from '../dev/cast';
import { TodayScreen } from '../features/today/components/TodayScreen';
import { renderApp } from './render';

const noop = () => {};
const vm = {
  session: { staff: cast.staff[0]!, facility: cast.facilities[0]! }, today: cast.today, loading: false, error: null, refreshing: false,
  onRefresh: noop, canConsult: true, onOpenAccount: noop, onOpenPatient: noop, onStartConsult: noop,
};

it('says Allergy on the allergy chip and words each queue state as the web does (MDV-P5-057)', async () => {
  await renderApp(<TodayScreen {...vm} />);
  expect(screen.getByText('Allergy: Penicillin')).toBeOnTheScreen();
  for (const words of ['In room', 'Ready for doctor', 'Checked in', 'Booked']) expect(screen.getByText(words)).toBeOnTheScreen();
});
