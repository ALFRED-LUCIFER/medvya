/// <reference types="node" />
// Test helper (Node only, never bundled): the audit action codes the server source writes, read from src/**/*.cs (MDV-P2-050), so a code
// added without a name in en, hi and ar fails auditLog.test.ts. A code is a dotted lowercase literal on a code line that mentions "audit" or
// "action" (an audit call, an "Action = …" initialiser, an "…Action = …" constant), or any such literal in a *Audit.cs file.
// shortcut: a heuristic over the source, not a registry; codes built from parts at run time are listed in `builtAtRunTime`, and literals on
// those lines that are not actions are in `notActions`. Move every code into one server registry if either list grows past a handful.
import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

const literal = /"([a-z0-9_-]+(?:\.[a-z0-9_-]+)+)"/g;

/** Codes made from parts at run time, with every value the parts can take (OrganisationStates.cs: organisation.state.{state}). */
export const builtAtRunTime = [
  'onboarding.complete.IN', 'onboarding.complete.AE',
  ...['Draft', 'Submitted', 'NeedsChanges', 'Approved', 'Rejected', 'Suspended'].map(s => `organisation.state.${s}`),
];

/** Dotted literals on audit lines that are not action codes: an HttpContext item key, a SettingsChange section. */
export const notActions = new Set(['audit.detail', 'staff.role']);

function* files(dir: string): Generator<string> {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    // The platform console has its own audit chain and words it itself; the practice log never shows it.
    if (['obj', 'bin', 'Migrations', 'Platform', 'Host.Platform'].includes(entry.name)) continue;
    const path = join(dir, entry.name);
    if (entry.isDirectory()) yield* files(path);
    else if (entry.name.endsWith('.cs')) yield path;
  }
}

export function serverAuditActions(srcRoot: string): string[] {
  const found = new Set<string>(builtAtRunTime);
  for (const file of files(srcRoot)) {
    const auditFile = file.endsWith('Audit.cs');
    for (const line of readFileSync(file, 'utf8').split('\n')) {
      const code = line.trim();
      if (code.startsWith('//') || code.startsWith('[LoggerMessage') || !(auditFile || /audit|action/i.test(code))) continue;
      for (const m of code.split('//')[0]!.matchAll(literal)) if (!notActions.has(m[1]!)) found.add(m[1]!);
    }
  }
  return [...found].sort();
}
