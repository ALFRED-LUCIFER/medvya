import { z } from 'zod';
import type { SexAtBirth } from '../../patients/domain/directory';

// Mirrors src/Modules/Prescribing/Medvya.Prescribing.Application/IOrders.cs (the server is the authority; these checks only save a round trip).
export const orderKinds = ['Lab', 'Imaging'] as const;
export type OrderKind = (typeof orderKinds)[number];
export type OrderState = 'Draft' | 'Ordered' | 'NotIncluded';
export type Order = { id: string; kind: OrderKind; name: string; note: string | null; state: OrderState; destination: string | null; orderedAt: string | null };
export type OrderSetChoice = { id: string; name: string; items: string[] };
export type OrderSlip = {
  patient: { fullName: string; mrn: string; dateOfBirth: string; sexAtBirth: SexAtBirth };
  facilityName: string; doctorName: string | null; orderedAt: string; destination: string | null; orders: Order[];
};

/** Column definitions are data; the label for each is the i18n key `orders.col.<key>`. */
export const orderColumns = ['test', 'kind', 'note', 'state', 'include'] as const;

/** State is an icon and a word, never colour alone. */
export const stateIcon = { Draft: 'pencil', Ordered: 'check', NotIncluded: 'minus' } as const satisfies Record<OrderState, string>;

/** Drafts can be switched off and on; a placed order cannot. */
export const canSwitch = (o: Order) => o.state !== 'Ordered';
export const isIncluded = (o: Order) => o.state !== 'NotIncluded';
/** "Place orders" has something to do only while there is an included draft. */
export const hasDrafts = (orders: Order[]) => orders.some(o => o.state === 'Draft');
export const hasPlaced = (orders: Order[]) => orders.some(o => o.state === 'Ordered');

export const orderSchema = z.object({ kind: z.enum(orderKinds), name: z.string().trim().min(1).max(200), note: z.string().trim().max(200) });
export type OrderForm = z.infer<typeof orderSchema>;
export const emptyOrder = (): Partial<OrderForm> => ({ kind: 'Lab', name: '', note: '' });
/** A blank note is not sent. */
export const toOrderBody = (f: OrderForm) => ({ kind: f.kind, name: f.name, note: f.note || null });

/** What a refusal is worded as (the screen has a sentence for each). */
export type OrderProblem = 'required' | 'too_long' | 'already_listed' | 'encounter_ended' | 'nothing_to_place' | 'not_draft' | 'failed';
const problems: readonly string[] = ['required', 'too_long', 'already_listed', 'encounter_ended', 'nothing_to_place', 'not_draft'];

/** The server's answer as a problem: a 409 carries a code; a 400 carries "field:code". Anything else reads as "failed". */
export function problemOf(code: string | undefined): OrderProblem {
  const reason = code?.includes(':') ? code.split(':')[1] : code;
  return reason && problems.includes(reason) ? (reason as OrderProblem) : 'failed';
}

/**
 * The Plan's test toggles (MDV-P2-043, design `inv`): the clinic's order-set tests and the visit's own, once each by name (as the server
 * matches them, ignoring case). `order` is the visit's row for that test, if any.
 */
export type TestToggle = { name: string; order: Order | null };
export function testToggles(sets: OrderSetChoice[], orders: Order[]): TestToggle[] {
  const byName = new Map<string, TestToggle>();
  for (const o of orders) if (!byName.has(o.name.toLowerCase()) || o.state !== 'NotIncluded') byName.set(o.name.toLowerCase(), { name: o.name, order: o });
  for (const name of sets.flatMap(s => s.items)) if (!byName.has(name.toLowerCase())) byName.set(name.toLowerCase(), { name, order: null });
  return [...byName.values()];
}
export const isOn = (x: TestToggle) => !!x.order && x.order.state !== 'NotIncluded';
/** On: re-include the visit's switched-off row, or add a new draft (once: the server refuses a second). Off: switch the draft off. An ordered test does not toggle. */
export function toggleOf(x: TestToggle, on: boolean): { add: string } | { include: string; on: boolean } | null {
  if (x.order?.state === 'Ordered') return null;
  if (x.order) return { include: x.order.id, on };
  return on ? { add: x.name } : null;
}
