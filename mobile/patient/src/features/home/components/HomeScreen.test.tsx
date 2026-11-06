import { I18nProvider } from '@medvya/i18n';
import { render, screen } from '@testing-library/react-native';
import { HomeScreen } from './HomeScreen';

it('shows the app name from the shared catalog', async () => {
  await render(<I18nProvider locale="en" country="IN"><HomeScreen /></I18nProvider>);
  expect(screen.getByRole('header')).toHaveTextContent('Medvya for patients');
});
