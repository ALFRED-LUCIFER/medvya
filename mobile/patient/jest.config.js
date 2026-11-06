// Workspace packages (@medvya/i18n) install their own React for their tests; map every import to the app's copy (Metro already does).
module.exports = {
  preset: 'jest-expo',
  moduleNameMapper: { '^react$': require.resolve('react'), '^react/(.*)$': `${require.resolve('react').replace(/index\.js$/, '')}$1` },
};
