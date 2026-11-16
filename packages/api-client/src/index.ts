import type { components, paths } from './schema';

/**
 * A schema as the server writes it. .NET's OpenAPI marks every integer "number or string" because the API reads either (JsonNumberHandling),
 * but it always writes a JSON number, so a response field is narrowed back to number here; nested objects and arrays follow.
 */
export type Wire<T> = [number] extends [T] ? ([string] extends [T] ? Exclude<T, string> : T)
  : T extends readonly (infer U)[] ? Wire<U>[]
  : T extends object ? { [K in keyof T]: Wire<T[K]> }
  : T;

/** Every response schema of Host.Api's OpenAPI document (`bash tools/check-contract.sh` regenerates schema.ts). */
export type Schemas = { [K in keyof components['schemas']]: Wire<components['schemas'][K]> };

export type { components, paths };
