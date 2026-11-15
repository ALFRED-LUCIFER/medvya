// Test stand-in for the Keychain / Keystore (wired in setup.ts). It keeps each item's options, so a test can see it is device-only.
export const WHEN_UNLOCKED_THIS_DEVICE_ONLY = 5;
export const keychain = new Map<string, { value: string; options?: { keychainAccessible?: number } }>();
export const secureStore = {
  WHEN_UNLOCKED_THIS_DEVICE_ONLY,
  getItemAsync: async (k: string) => keychain.get(k)?.value ?? null,
  setItemAsync: async (k: string, value: string, options?: { keychainAccessible?: number }) => { keychain.set(k, { value, options }); },
  deleteItemAsync: async (k: string) => { keychain.delete(k); },
};
