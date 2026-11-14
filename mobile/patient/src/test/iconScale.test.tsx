import { Button, Chip, Icon } from '@medvya/ui-native';
import { screen } from '@testing-library/react-native';
import { Dimensions } from 'react-native';
import { cast } from '../dev/cast';
import { VisitsScreen } from '../features/visits/components/VisitsScreen';
import { renderApp } from './render';

// The icon font renders a glyph; a stub shows the size it was given instead.
jest.mock('@expo/vector-icons/MaterialIcons', () => {
  const { createElement } = require('react') as typeof import('react');
  const { Text } = require('react-native') as typeof import('react-native');
  return { __esModule: true, default: ({ size }: { size: number }) => createElement(Text, { testID: 'icon' }, String(size)) };
});

const window = Dimensions.get('window');
const sizes = () => screen.getAllByTestId('icon').map(i => Number(i.props.children));
afterEach(() => Dimensions.set({ window: { ...window, fontScale: 1 } }));

// MDV-P5-063: at large system font, chip and button icons grow with their text up to a cap; tab bar icons stay fixed.
it.each([[1, [15, 20, 22]], [1.3, [20, 26, 22]], [2, [23, 30, 22]]])('sizes icons for font scale %s', async (fontScale, expected) => {
  Dimensions.set({ window: { ...window, fontScale } });
  await renderApp(<><Chip label="Booked" icon="check" /><Button label="Book a visit" icon="add" /><Icon name="home" fixed /></>);
  expect(sizes()).toEqual(expected);
});

it('titles the Book tab screen "Book", not the Records "Visits" name', async () => {
  await renderApp(<VisitsScreen session={{ person: cast.people[0]!, phone: '+919000000101', clinic: cast.clinic }}
    visits={{ upcoming: [], past: [] }} loading={false} error={null} />);
  expect(screen.getByRole('header', { name: 'Book' })).toBeOnTheScreen();
  expect(screen.queryByText('Visits')).toBeNull();
});
