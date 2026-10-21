import { describe, expect, it } from 'vitest';
import { addressLines, asSearchError, pageCount, toQuery } from './directory';

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
