import { Button, Screen } from '@medvya/ui-native';
import { act, fireEvent, screen } from '@testing-library/react-native';
import { Keyboard, StyleSheet, type KeyboardEvent } from 'react-native';
import { renderApp } from './render';

// MDV-P5-060: with the keyboard up, the pinned footer (sign-in's Continue) is lifted above it, not covered.
it('lifts the footer above the keyboard', async () => {
  const listeners: Record<string, (e: KeyboardEvent | null) => void> = {};
  const real = Keyboard.addListener.bind(Keyboard);
  jest.spyOn(Keyboard, 'addListener').mockImplementation((name, fn) => { listeners[name] = fn as (e: KeyboardEvent | null) => void; return real(name, fn); });
  await renderApp(<Screen footer={<Button label="Continue" />}>{null}</Screen>);

  // The nearest ancestor of the footer that measures itself to make room for the keyboard.
  let box = screen.getByRole('button', { name: 'Continue' }).parent;
  while (box && !box.props.onLayout) box = box.parent;
  expect(box).toBeTruthy();
  await fireEvent(box!, 'layout', { nativeEvent: { layout: { x: 0, y: 0, width: 390, height: 800 } }, persist: () => {} });
  await act(async () => {
    listeners.keyboardWillShow?.({ endCoordinates: { screenX: 0, screenY: 500, width: 390, height: 300 }, duration: 0, easing: 'keyboard' } as KeyboardEvent);
  });
  expect(StyleSheet.flatten(box!.props.style).paddingBottom).toBe(300);
});
