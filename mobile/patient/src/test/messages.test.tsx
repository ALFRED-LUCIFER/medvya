import { screen } from '@testing-library/react-native';
import { cast } from '../dev/cast';
import { MessagesScreen } from '../features/messages/components/MessagesScreen';
import { renderApp } from './render';

// MDV-P5-058: while sending is not built, nothing typed may be lost, and the reason shows on screen.
it('disables the message field with the send button and says why', async () => {
  await renderApp(<MessagesScreen session={{ person: cast.people[0]!, phone: '+919000000101', clinic: cast.clinic }} messages={[]} loading={false} error={null} />);
  const field = screen.getByLabelText('Message');
  expect(field.props.editable).toBe(false);
  expect(field).toBeDisabled();
  expect(screen.getByRole('button', { name: 'Send' })).toBeDisabled();
  expect(screen.getByText('Available in a later release', { includeHiddenElements: true })).toBeOnTheScreen();
});
