// Every test runs against in-memory stand-ins for the phone's secure-store, encrypted SQLite and crypto (fakes.ts).
jest.mock('expo-secure-store', () => require('./fakes').secureStore);
jest.mock('expo-sqlite', () => require('./fakes').sqlite);
jest.mock('expo-crypto', () => require('./fakes').crypto);
jest.mock('expo-screen-capture', () => ({ preventScreenCaptureAsync: async () => {}, enableAppSwitcherProtectionAsync: async () => {} }));
