import { catalogs } from '@medvya/i18n';
import ar from '@medvya/i18n/messages/ar.json';
import { screen } from '@testing-library/react-native';
import { cast } from '../dev/cast';
import { AccountScreen } from '../features/account/components/AccountScreen';
import { InboxScreen } from '../features/inbox/components/InboxScreen';
import { PatientRecordScreen } from '../features/patients/components/PatientRecordScreen';
import { PatientsScreen } from '../features/patients/components/PatientsScreen';
import { TodayScreen } from '../features/today/components/TodayScreen';
import { renderApp } from './render';

// In Arabic the layout is right to left, but dates, phone numbers and emails stay left to right inside it (MDV-P5-032):
// each sits in a left-to-right Unicode isolate (U+2066 … U+2069), the native equivalent of the web's <bdi dir="ltr">.
const session = { staff: cast.staff[0]!, facility: cast.facilities[0]! };
const noop = () => {};
const isolated = (s: string) => new RegExp(`⁦${s}⁩`);

// Jest cannot run the catalog's dynamic import, so the Arabic catalog is put in place directly.
beforeAll(() => { catalogs.ar = ar; });

it('keeps the masked phone number left to right in the patient list', async () => {
  await renderApp(<PatientsScreen session={session} query="" onQuery={noop} patients={cast.patients} loading={false} error={null} onOpenAccount={noop} onOpenPatient={noop} />, 'ar');
  expect(screen.getByText(isolated('••4471'))).toBeOnTheScreen();
});

it('keeps the date of birth left to right on the patient record', async () => {
  await renderApp(<PatientRecordScreen record={cast.records['p-meera']!} clinical loading={false} notFound={false} error={null} onBack={noop} />, 'ar');
  expect(screen.getByText(/الميلاد ⁦\d{2} .+ \d{4}⁩$/)).toBeOnTheScreen();
});

it('keeps the reply-by time left to right in the inbox', async () => {
  await renderApp(<InboxScreen session={session} inbox={cast.inbox} clinical loading={false} error={null} onOpenAccount={noop} />, 'ar');
  expect(screen.getByText(isolated(cast.inbox.messages[0]!.replyBy))).toBeOnTheScreen();
});

it('keeps the email left to right on the account screen', async () => {
  await renderApp(<AccountScreen staff={session.staff} facility={session.facility} biometric unlockMethod="Face ID" locale="ar" version="1.0.0"
    onLock={noop} onSignOut={noop} onBack={noop} />, 'ar');
  expect(screen.getByText(isolated(session.staff.email.replace('.', '\\.')))).toBeOnTheScreen();
});

it('keeps the queue token left to right on Today, on Android as well as iOS', async () => {
  await renderApp(<TodayScreen session={session} today={cast.today} loading={false} error={null} refreshing={false} onRefresh={noop} canConsult offlineSince={null}
    onOpenAccount={noop} onOpenPatient={noop} onStartConsult={noop} />, 'ar');
  expect(screen.getByText(isolated('A-09'))).toBeOnTheScreen();
});
