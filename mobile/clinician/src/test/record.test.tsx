import { screen } from '@testing-library/react-native';
import { cast } from '../dev/cast';
import { PatientRecordScreen } from '../features/patients/components/PatientRecordScreen';
import { renderApp } from './render';

const meera = cast.records['p-meera']!;
const base = { loading: false, notFound: false, error: null, onBack: () => {} };

it('shows an allergy as words and a verified badge, not colour alone', async () => {
  await renderApp(<PatientRecordScreen {...base} record={meera} clinical />);
  expect(screen.getByRole('alert')).toHaveTextContent(/Allergy: Penicillin \(rash\)/);
  expect(screen.getAllByText('Verified').length).toBeGreaterThan(0);
  expect(screen.getByText('Since last visit')).toBeOnTheScreen();
});

it('keeps clinical details from front desk but still shows the allergy', async () => {
  await renderApp(<PatientRecordScreen {...base} record={meera} clinical={false} />);
  expect(screen.getByRole('alert')).toHaveTextContent(/Penicillin/);
  expect(screen.queryByText('Since last visit')).toBeNull();
  expect(screen.getByText('Clinical details are for clinicians.')).toBeOnTheScreen();
});
