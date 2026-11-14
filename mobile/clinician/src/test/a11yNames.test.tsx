import { ltr } from '@medvya/i18n';
import { Icon } from '@medvya/ui-native';
import { render, screen } from '@testing-library/react-native';
import { cast } from '../dev/cast';
import { InboxScreen } from '../features/inbox/components/InboxScreen';
import { PatientsScreen } from '../features/patients/components/PatientsScreen';
import { TodayScreen } from '../features/today/components/TodayScreen';
import { renderApp } from './render';

// Screen readers hear the flag's words, never an icon glyph or colour alone (MDV-P5-055, audit M-01, M-02, M-10).
const session = { staff: cast.staff[0]!, facility: cast.facilities[0]! };
const noop = () => {};

it('puts the allergy and critical flags in the patient row names', async () => {
  await renderApp(<PatientsScreen session={session} query="" onQuery={noop} patients={cast.patients} loading={false} error={null} onOpenAccount={noop} onOpenPatient={noop} />);
  expect(screen.getByRole('button', { name: `Meera Iyer, 54 F · ${ltr('••4471')}, Allergy` })).toBeOnTheScreen();
  expect(screen.getByRole('button', { name: /^Rahul Menon, .*, Critical$/ })).toBeOnTheScreen();
});

it('names the patient and test on each Mark reviewed', async () => {
  await renderApp(<InboxScreen session={session} inbox={cast.inbox} clinical loading={false} error={null} onOpenAccount={noop} />);
  expect(screen.getByRole('button', { name: 'Mark reviewed: Rahul Menon, Potassium' })).toBeOnTheScreen();
  expect(screen.getByRole('button', { name: 'Mark reviewed: Meera Iyer, HbA1c' })).toBeOnTheScreen();
});

it('names whom each Reply answers, so two Reply buttons never share a name', async () => {
  const inbox = { ...cast.inbox, messages: [...cast.inbox.messages, { id: 'm2', patient: 'Joseph Mathew', initials: 'JM', text: 'Is the report ready?', replyBy: '17:00' }] };
  await renderApp(<InboxScreen session={session} inbox={inbox} clinical={false} loading={false} error={null} onOpenAccount={noop} />);
  expect(screen.getByRole('button', { name: 'Reply to Meera Iyer' })).toBeOnTheScreen();
  expect(screen.getByRole('button', { name: 'Reply to Joseph Mathew' })).toBeOnTheScreen();
});

it('reads the Up next card as one label with every flag in it, not chip by chip', async () => {
  await renderApp(<TodayScreen session={session} today={cast.today} loading={false} error={null} refreshing={false} onRefresh={noop} canConsult
    onOpenAccount={noop} onOpenPatient={noop} onStartConsult={noop} />);
  const card = screen.getByLabelText('Up next, Meera Iyer, 54 F · Diabetes follow-up · Room 3, Allergy: Penicillin, HbA1c 7.9 %, Brief · not reviewed');
  expect(card.props.accessible).toBe(true);
});

it('keeps icon glyphs out of the accessibility tree on both platforms', async () => {
  const glyph = (await render(<Icon name="inbox" />)).toJSON();
  expect(glyph && !Array.isArray(glyph) ? glyph.props : null).toMatchObject({ accessible: false, importantForAccessibility: 'no', accessibilityElementsHidden: true });
});
