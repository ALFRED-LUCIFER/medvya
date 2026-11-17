// @vitest-environment jsdom
import { expect, it } from 'vitest';
import { rememberedFilters, rememberFilters } from './timelineApi';

it('keeps each user\'s timeline filters apart on a shared computer', () => {
  const unfiltered = rememberedFilters('u-2');
  rememberFilters('u-1', { ...unfiltered, kinds: ['Medicine'] });
  expect(rememberedFilters('u-1')).not.toEqual(unfiltered);
  expect(rememberedFilters('u-2')).toEqual(unfiltered);
});
