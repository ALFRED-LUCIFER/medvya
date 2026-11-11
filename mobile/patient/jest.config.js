// Workspace packages (@medvya/i18n) install their own React for their tests; map every import to the app's copy (Metro already does).
module.exports = {
  // The first render in a file pays the cold transform of expo and react-native: 0.6 s idle, over 5 s on a shared CI runner (#930).
  testTimeout: 20000,
  preset: 'jest-expo',
  moduleNameMapper: { '^react$': require.resolve('react'), '^react/(.*)$': `${require.resolve('react').replace(/index\.js$/, '')}$1` },
};
