import * as Crypto from 'expo-crypto';
import * as SecureStore from 'expo-secure-store';
import * as SQLite from 'expo-sqlite';

/**
 * The encrypted on-device store (ADR-011 §3): a read-only last copy of today's list, in SQLCipher, with its key in
 * secure-store. Copies older than 24 hours are never served and are deleted. Nothing else of a patient is kept on the phone.
 * shortcut: an expired copy is deleted on the next read, at sign-out or at launch, not on a timer; add a purge on
 * foreground when MDV-P5-033 lets a session outlive the app process.
 */
const DB = 'medvya-copy.db';
const KEY = 'medvya.copyKey';
export const KEEP_MS = 24 * 60 * 60 * 1000;
export const deviceOnly = { keychainAccessible: SecureStore.WHEN_UNLOCKED_THIS_DEVICE_ONLY };

let db: Promise<SQLite.SQLiteDatabase> | null = null;
let generation = 0;
let queue: Promise<unknown> = Promise.resolve();
/** One store operation at a time, so a wipe never interleaves with a write. */
function serial<T>(op: () => Promise<T>): Promise<T> {
  const next = queue.then(op);
  queue = next.catch(() => undefined);
  return next;
}

const hex = (bytes: Uint8Array) => Array.from(bytes, b => b.toString(16).padStart(2, '0')).join('');

/** Closes and deletes the database file. Opening first makes sure there is a file, so a real delete failure is not hidden. */
async function drop(open: SQLite.SQLiteDatabase | null) {
  await (open ?? await SQLite.openDatabaseAsync(DB)).closeAsync();
  await SQLite.deleteDatabaseAsync(DB);
}

async function open() {
  let key = await SecureStore.getItemAsync(KEY);
  if (!key) {
    await drop(null); // a file left without its key (reinstall, restored backup) can never be read: start clean
    key = hex(Crypto.getRandomBytes(32));
    await SecureStore.setItemAsync(KEY, key, deviceOnly);
  }
  if (!/^[0-9a-f]{64}$/.test(key)) throw new Error('mobile.error.generic');
  const conn = await SQLite.openDatabaseAsync(DB);
  // PRAGMA takes no bound parameters; the key is 64 hex characters this app generated (checked above), never user input.
  await conn.execAsync(`PRAGMA key = "x'${key}'"`);
  const cipher = await conn.getFirstAsync<{ cipher_version: string }>('PRAGMA cipher_version');
  if (!cipher?.cipher_version) { await drop(conn); throw new Error('mobile.error.generic'); } // a build without SQLCipher never writes PHI in the clear
  await conn.execAsync('CREATE TABLE IF NOT EXISTS copy (key TEXT PRIMARY KEY NOT NULL, body TEXT NOT NULL, saved_at INTEGER NOT NULL)');
  return conn;
}
const connection = () => (db ??= open().catch(e => { db = null; throw e; }));

/** Marks the start of a fetch; a copy fetched before a wipe is then dropped instead of saved. */
export const storeGeneration = () => generation;

/** Keeps `body` as the last copy for `key`, unless the store was wiped since `since` (a sign-out raced the fetch). */
export const saveCopy = (key: string, body: unknown, since: number, now = Date.now()) => serial(async () => {
  if (since !== generation) return;
  await (await connection()).runAsync('INSERT OR REPLACE INTO copy (key, body, saved_at) VALUES (?, ?, ?)', key, JSON.stringify(body), now);
});

/** The last copy for `key` if it is younger than 24 hours. Older copies are deleted on the way (ADR-011 §3). */
export const readCopy = <T>(key: string, now = Date.now()) => serial(async () => {
  const conn = await connection();
  await conn.runAsync('DELETE FROM copy WHERE saved_at <= ?', now - KEEP_MS);
  const row = await conn.getFirstAsync<{ body: string; saved_at: number }>('SELECT body, saved_at FROM copy WHERE key = ?', key);
  return row ? { body: JSON.parse(row.body) as T, savedAt: row.saved_at } : null;
});

/** Deletes the database file and its key. After this nothing the store held can be read again. */
export function wipeStore() {
  generation++;
  return serial(async () => {
    const open = await db?.catch(() => null) ?? null;
    db = null;
    await drop(open);
    await SecureStore.deleteItemAsync(KEY);
  });
}
