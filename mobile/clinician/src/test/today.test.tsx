import MaterialIcons from '@expo/vector-icons/MaterialIcons';
import { medicineGlyph, statuses } from '@medvya/tokens/status';
import { screen, within } from '@testing-library/react-native';
import { cast } from '../dev/cast';
import { TodayScreen } from '../features/today/components/TodayScreen';
import { renderApp } from './render';

const noop = () => {};
const vm = {
  session: { staff: cast.staff[0]!, facility: cast.facilities[0]! }, today: cast.today, loading: false, error: null, refreshing: false,
  onRefresh: noop, canConsult: true, offlineSince: null,onOpenAccount: noop, onOpenPatient: noop, onStartConsult: noop,
};

it('says Allergy on the allergy chip and words each queue state as the web does (MDV-P5-057)', async () => {
  await renderApp(<TodayScreen {...vm} />);
  expect(screen.getByText('Allergy: Penicillin')).toBeOnTheScreen();
  // The chip draws the status map's allergy glyph (the icon font renders it as its code point), which is never the medicine glyph (AC2).
  const glyph = String.fromCodePoint(Number(MaterialIcons.getRawGlyphMap()[statuses.allergy.native]));
  expect(within(screen.getByText('Allergy: Penicillin').parent!).getByText(glyph, { includeHiddenElements: true })).toBeOnTheScreen();
  expect(statuses.allergy.native).not.toBe(medicineGlyph.native);
  for (const words of ['In room', 'Ready for doctor', 'Checked in', 'Booked']) expect(screen.getByText(words)).toBeOnTheScreen();
});
