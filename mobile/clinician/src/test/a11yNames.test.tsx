import { Icon } from '@medvya/ui-native';
import { render, screen } from '@testing-library/react-native';
import { cast } from '../dev/cast';
import { InboxScreen } from '../features/inbox/components/InboxScreen';
import { PatientsScreen } from '../features/patients/components/PatientsScreen';
import { renderApp } from './render';

// Screen readers hear the flag's words, never an icon glyph or colour alone (MDV-P5-055, audit M-01, M-02, M-10).
const session = { staff: cast.staff[0]!, facility: cast.facilities[0]! };
const noop = () => {};

it('puts the allergy and critical flags in the patient row names', async () => {
  await renderApp(<PatientsScreen session={session} query="" onQuery={noop} patients={cast.patients} loading={false} error={null} onOpenAccount={noop} onOpenPatient={noop} />);
  expect(screen.getByRole('button', { name: 'Meera Iyer, 54 F · ••4471, Allergy' })).toBeOnTheScreen();
  expect(screen.getByRole('button', { name: /^Rahul Menon, .*, Critical$/ })).toBeOnTheScreen();
});

it('names the patient and test on each Mark reviewed', async () => {
  await renderApp(<InboxScreen session={session} inbox={cast.inbox} clinical loading={false} error={null} onOpenAccount={noop} />);
  expect(screen.getByRole('button', { name: 'Mark reviewed: Rahul Menon, Potassium' })).toBeOnTheScreen();
  expect(screen.getByRole('button', { name: 'Mark reviewed: Meera Iyer, HbA1c' })).toBeOnTheScreen();
});

it('keeps icon glyphs out of the accessibility tree on both platforms', async () => {
  const glyph = (await render(<Icon name="inbox" />)).toJSON();
  expect(glyph && !Array.isArray(glyph) ? glyph.props : null).toMatchObject({ accessible: false, importantForAccessibility: 'no', accessibilityElementsHidden: true });
});
