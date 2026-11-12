import { describe, expect, it } from 'vitest';
import { canSwitch, hasDrafts, hasPlaced, isOn, orderSchema, problemOf, testToggles, toggleOf, toOrderBody, type Order } from './orders';

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

describe('plan test toggles (MDV-P2-043)', () => {
  const o = (name: string, state: Order['state'], id = name): Order => ({ id, kind: 'Lab', name, note: null, state, destination: null, orderedAt: null });
  const sets = [{ id: 's', name: 'Diabetes', items: ['HbA1c', 'Lipid profile', 'Urine albumin'] }];

  it('lists each test once, the visit\'s own row first', () => {
    const list = testToggles(sets, [o('hba1c', 'Draft'), o('Retinal photo', 'Ordered')]);
    expect(list.map(x => [x.name, isOn(x)])).toEqual([['hba1c', true], ['Retinal photo', true], ['Lipid profile', false], ['Urine albumin', false]]);
  });

  it('on adds a draft once or re-includes it; off switches the draft off; an ordered test never toggles', () => {
    expect(toggleOf({ name: 'Lipid profile', order: null }, true)).toEqual({ add: 'Lipid profile' });
    expect(toggleOf({ name: 'Lipid profile', order: null }, false)).toBeNull();
    expect(toggleOf({ name: 'HbA1c', order: o('HbA1c', 'NotIncluded', 'x') }, true)).toEqual({ include: 'x', on: true });
    expect(toggleOf({ name: 'HbA1c', order: o('HbA1c', 'Draft', 'x') }, false)).toEqual({ include: 'x', on: false });
    expect(toggleOf({ name: 'HbA1c', order: o('HbA1c', 'Ordered', 'x') }, false)).toBeNull();
  });
});

