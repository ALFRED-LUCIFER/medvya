// Test stand-ins for the native storage modules (wired in setup.ts), backed by Node's own SQLite and crypto.
// Typed by hand: the app has no @types/node, and these four calls are all the fakes need.
type Statement = { run: (...p: unknown[]) => unknown; get: (...p: unknown[]) => unknown };
type NodeDb = { exec: (sql: string) => void; prepare: (sql: string) => Statement; close: () => void };
const { DatabaseSync } = jest.requireActual<{ DatabaseSync: new (path: string) => NodeDb }>('node:sqlite');
const node = jest.requireActual<{
  createHash: (alg: string) => { update: (s: string) => { digest: (enc: 'hex') => string } };
  randomBytes: (n: number) => Uint8Array;
  randomUUID: () => string;
}>('node:crypto');

/** What the Keychain / Keystore holds, so a test can see it empty after a wipe. */
export const keychain = new Map<string, string>();
export const secureStore = {
  WHEN_UNLOCKED_THIS_DEVICE_ONLY: 5,
  getItemAsync: async (k: string) => keychain.get(k) ?? null,
  setItemAsync: async (k: string, v: string) => { keychain.set(k, v); },
  deleteItemAsync: async (k: string) => { keychain.delete(k); },
};

/** Database files on the phone. Strict like the device: deleting a missing or still-open database fails. */
export const files = new Map<string, { db: NodeDb; open: boolean }>();
export const sqlite = {
  openDatabaseAsync: async (name: string) => {
    const file = files.get(name) ?? { db: new DatabaseSync(':memory:'), open: false };
    files.set(name, file);
    file.open = true;
    return {
      execAsync: async (sql: string) => { if (!sql.startsWith('PRAGMA key')) file.db.exec(sql); }, // Node's SQLite has no SQLCipher
      runAsync: async (sql: string, ...params: unknown[]) => { file.db.prepare(sql).run(...params); },
      getFirstAsync: async (sql: string, ...params: unknown[]) => (sql === 'PRAGMA cipher_version' ? { cipher_version: 'test' } : file.db.prepare(sql).get(...params) ?? null),
      closeAsync: async () => { file.open = false; },
    };
  },
  deleteDatabaseAsync: async (name: string) => {
    const file = files.get(name);
    if (!file) throw new Error(`Database ${name} not found`);
    if (file.open) throw new Error(`Database ${name} is open`);
    file.db.close();
    files.delete(name);
  },
};

export const crypto = {
  CryptoDigestAlgorithm: { SHA256: 'SHA-256' },
  getRandomBytes: (n: number) => new Uint8Array(node.randomBytes(n)),
  randomUUID: () => node.randomUUID(),
  digestStringAsync: async (_alg: string, data: string) => node.createHash('sha256').update(data).digest('hex'),
};
