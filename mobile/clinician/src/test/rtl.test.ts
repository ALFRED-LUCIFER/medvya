import app from '../../app.json';

// Arabic lays out right to left only when the app opts in: a build reads the plugin option, Expo Go reads it from `extra` (MDV-P5-056).
it('opts in to right-to-left layout for Arabic', () => {
  const localization = app.expo.plugins.find(p => Array.isArray(p) && p[0] === 'expo-localization');
  expect(localization?.[1]).toMatchObject({ supportsRTL: true });
});
