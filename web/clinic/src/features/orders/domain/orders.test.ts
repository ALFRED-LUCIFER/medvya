import { describe, expect, it } from 'vitest';
import { canSwitch, hasDrafts, hasPlaced, orderSchema, problemOf, toOrderBody, type Order } from './orders';

const order = (state: Order['state']): Order => ({ id: state, kind: 'Lab', name: 'HbA1c', note: null, state, destination: null, orderedAt: null });

describe('order rules', () => {
  it('lets a draft be switched and never an ordered test', () => {
    expect(canSwitch(order('Draft'))).toBe(true);
    expect(canSwitch(order('NotIncluded'))).toBe(true);
    expect(canSwitch(order('Ordered'))).toBe(false);
  });

  it('places only when there is an included draft, and shows the slip only after something was placed', () => {
    expect(hasDrafts([order('NotIncluded'), order('Ordered')])).toBe(false);
    expect(hasDrafts([order('Draft')])).toBe(true);
    expect(hasPlaced([order('Draft')])).toBe(false);
    expect(hasPlaced([order('Ordered')])).toBe(true);
  });

  it('refuses a missing test name and sends no blank note', () => {
    expect(orderSchema.safeParse({ kind: 'Lab', name: ' ', note: '' }).success).toBe(false);
    expect(toOrderBody(orderSchema.parse({ kind: 'Imaging', name: ' Chest X-ray ', note: '' }))).toEqual({ kind: 'Imaging', name: 'Chest X-ray', note: null });
  });

  it('reads the server problem as a sentence the screen has', () => {
    expect(problemOf('nothing_to_place')).toBe('nothing_to_place');
    expect(problemOf('name:required')).toBe('required');
    expect(problemOf('surprise')).toBe('failed');
    expect(problemOf(undefined)).toBe('failed');
  });
});
