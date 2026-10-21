import { readFileSync } from 'node:fs';
import { test } from '@playwright/test';

// Steps come from the storyboard doc itself (generated from the design), so specs cannot drift from it.
const doc = readFileSync(new URL('../../../docs/02-design/journeys.md', import.meta.url), 'utf8');

export type Step = { n: number; screen: string; role: string; step: string; changes: string };

export function storyboard(id: string): { title: string; milestone: string; steps: Step[] } {
  const section = doc.split(/^## /m).find(s => s.startsWith(`${id} `));
  if (!section) throw new Error(`Journey ${id} not found in docs/02-design/journeys.md`);
  const title = section.split('\n')[0]!;
  const steps = section.split('\n')
    .filter(l => /^\| \d+ \|/.test(l))
    .map(l => l.split('|').slice(1, -1).map(c => c.trim()))
    .map(([n, screen, role, step, changes]) => ({ n: Number(n), screen: screen!, role: role!, step: step!, changes: changes! }));
  return { title, milestone: title.match(/M\d(?:–M\d)?$/)?.[0] ?? '?', steps };
}

/**
 * One test per storyboard step. Steps without an implementation are `fixme`, tagged with screen and milestone,
 * so each phase turns its own steps green by passing an implementation for that step number.
 */
export function journey(id: string, implemented: Record<number, Parameters<typeof test>[2]> = {}) {
  const { title, milestone, steps } = storyboard(id);
  test.describe(title, () => {
    for (const s of steps) {
      const name = `${id}.${s.n} ${s.screen} · ${s.role}: ${s.step}`;
      const tag = [`@${s.screen}`, `@${milestone.replace('–', '-')}`];
      const body = implemented[s.n];
      if (body) test(name, { tag }, body);
      else test.fixme(name, { tag }, () => {});
    }
  });
}
