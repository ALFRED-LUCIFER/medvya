// Every test runs against an in-memory stand-in for the phone's secure-store (fakes.ts).
jest.mock('expo-secure-store', () => require('./fakes').secureStore);
