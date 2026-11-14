import { describe, expect, it } from 'vitest';
import { addressLines, asFilter, asSearchError, missingParts, pageCount, toQuery } from './directory';

describe('patient directory rules', () => {
  it('treats a blank search as the directory list and trims the rest', () => {
    expect(toQuery('   ')).toBeNull();
    expect(toQuery('  meera ')).toBe('meera');
  });

  it('counts at least one page', () => {
    expect(pageCount({ total: 0, pageSize: 20 })).toBe(1);
    expect(pageCount({ total: 41, pageSize: 20 })).toBe(3);
  });

  it('knows only the server search codes', () => {
    expect(asSearchError('full_number_required')).toBe('full_number_required');
    expect(asSearchError('something_else')).toBeNull();
    expect(asSearchError(undefined)).toBeNull();
  });

  it('drops empty address parts', () => {
    expect(addressLines({ line1: 'Villa 1 (synthetic)', line2: 'Al Reem', city: null, region: 'AUH' })).toEqual(['Villa 1 (synthetic)', 'Al Reem', 'AUH']);
  });
});

describe('missing registration parts', () => {
  it('lists only what was not entered, in the order address, payer, emergency contact', () => {
    expect(missingParts({ address: null, payer: null, emergencyContact: null })).toEqual(['address', 'payer', 'emergencyContact']);
    expect(missingParts({ address: { line1: 'x' }, payer: null, emergencyContact: { name: 'x' } })).toEqual(['payer']);
    expect(missingParts({ address: {}, payer: {}, emergencyContact: {} })).toEqual([]);
  });

  it('reads only a known filter from the address (Clinic today opens Incomplete)', () => {
    expect([asFilter('Incomplete'), asFilter('Recent'), asFilter('incomplete'), asFilter(null), asFilter('<script>')]).toEqual(['Incomplete', 'Recent', 'All', 'All', 'All']);
  });
});
