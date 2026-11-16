import { z } from 'zod';

// Mirrors src/Modules/Billing/Medvya.Billing.Application/ICatalogue.cs (MDV-P4-002). The server is the authority; these checks only save a round trip.
export const serviceCategories = ['Consultation', 'Procedure', 'Test', 'Other'] as const;
export type ServiceCategory = (typeof serviceCategories)[number];
export const taxTreatments = ['Exempt', 'Taxable'] as const;
export type TaxTreatment = (typeof taxTreatments)[number];
export type ConsultationKind = 'New' | 'FollowUp';
export type Currency = 'INR' | 'AED';

export type Service = {
  id: string; name: string; category: ServiceCategory; code: string | null; sac: string | null; taxTreatment: TaxTreatment;
  gstRateBasisPoints: number | null; durationMinutes: number | null; consultationKind: ConsultationKind | null; archivedAt: string | null;
  /** The clinic default price in effect now; null when the service has none yet (it cannot be billed). */
  clinicPriceMinor: number | null; currency: Currency; version: number;
};
/** doctorId null: the clinic default. Immutable: a new price is a new version. */
export type PriceVersion = { id: string; doctorId: string | null; amountMinor: number; currency: Currency; effectiveFrom: string; createdAt: string };

/** Column definitions are data; the label for each is the i18n key `billing.services.col.<key>`. */
export const serviceColumns = ['name', 'category', 'sac', 'tax', 'price'] as const;
export type ServiceColumn = (typeof serviceColumns)[number];
export const serviceSort = {
  name: (s: Service) => s.name,
  category: (s: Service) => serviceCategories.indexOf(s.category),
  price: (s: Service) => s.clinicPriceMinor,
};
export type ServiceSortColumn = keyof typeof serviceSort;
export const isServiceSortable = (c: ServiceColumn): c is ServiceSortColumn => c in serviceSort;

/** Whole currency units with up to two decimals in the form; integer minor units on the wire (AGENTS.md 3.4). */
const amount = z.string().trim().regex(/^\d{1,7}(\.\d{1,2})?$/);
export const toMinor = (major: string) => Math.round(Number(major) * 100);

export const serviceSchema = z.object({
  name: z.string().trim().min(1).max(200),
  category: z.enum(serviceCategories),
  code: z.string().trim().max(20),
  sac: z.string().trim().regex(/^(\d{6})?$/),
  taxTreatment: z.enum(taxTreatments),
  /** A percentage in the form (18 or 18.5); basis points on the wire. */
  gstRate: z.string().trim(),
  durationMinutes: z.string().trim().regex(/^(\d{1,3})?$/),
}).refine(f => f.taxTreatment === 'Exempt' || (/^\d{1,2}(\.\d{1,2})?$/.test(f.gstRate) && Number(f.gstRate) <= 28), { path: ['gstRate'] });
export type ServiceForm = z.infer<typeof serviceSchema>;
export const emptyService: ServiceForm = { name: '', category: 'Procedure', code: '', sac: '', taxTreatment: 'Exempt', gstRate: '', durationMinutes: '' };
export const toServiceForm = (s: Service): ServiceForm => ({
  name: s.name, category: s.category, code: s.code ?? '', sac: s.sac ?? '', taxTreatment: s.taxTreatment,
  gstRate: s.gstRateBasisPoints === null ? '' : String(s.gstRateBasisPoints / 100), durationMinutes: s.durationMinutes === null ? '' : String(s.durationMinutes),
});
/** A blank stays blank; the consultation kind is not edited here, so an edit sends back the one the service has. */
export const toServiceBody = (f: ServiceForm, s?: Service) => ({
  name: f.name, category: f.category, code: f.code || null, sac: f.sac || null, taxTreatment: f.taxTreatment,
  gstRateBasisPoints: f.taxTreatment === 'Taxable' ? Math.round(Number(f.gstRate) * 100) : null,
  durationMinutes: f.durationMinutes ? Number(f.durationMinutes) : null,
  consultationKind: s?.consultationKind ?? null, version: s?.version ?? null,
});

export const priceSchema = z.object({ amount, effectiveOn: z.iso.date() });
export type PriceForm = z.infer<typeof priceSchema>;
export const toPriceBody = (doctorId: string | null, f: PriceForm) => ({ doctorId, amountMinor: toMinor(f.amount), effectiveOn: f.effectiveOn });

/** Today in the facility's time zone as YYYY-MM-DD: prices start on a facility day, never the device's. */
export const todayIn = (timeZone: string, now = new Date()) => new Intl.DateTimeFormat('en-CA', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit' }).format(now);

/** One row of the editor's price list: the clinic default (doctorId null) or one doctor at one location. */
export type PriceRow = { doctorId: string | null; name: string | null; location: string | null; current: PriceVersion | null; upcoming: PriceVersion | null };

/** The version in effect at `at` for a doctor (their own, else the clinic default), as the server resolves an invoice line. */
export function priceFor(versions: PriceVersion[], doctorId: string | null, at: Date): PriceVersion | null {
  const inEffect = versions.filter(v => new Date(v.effectiveFrom) <= at)
    .sort((a, b) => b.effectiveFrom.localeCompare(a.effectiveFrom) || b.createdAt.localeCompare(a.createdAt));
  return (doctorId ? inEffect.find(v => v.doctorId === doctorId) : undefined) ?? inEffect.find(v => v.doctorId === null) ?? null;
}

/** The next version waiting for its day, if any, for one row of the price list (the doctor's own, or the clinic default). */
export const upcomingFor = (versions: PriceVersion[], doctorId: string | null, at: Date): PriceVersion | null =>
  versions.filter(v => v.doctorId === doctorId && new Date(v.effectiveFrom) > at).sort((a, b) => a.effectiveFrom.localeCompare(b.effectiveFrom))[0] ?? null;

/** What one line of a bill of supply shows for this service at this price (MDV-P4-004 prints it); tax 0 for an exempt service. */
export function linePreview(s: Pick<Service, 'name' | 'sac' | 'taxTreatment' | 'gstRateBasisPoints'>, unitMinor: number) {
  const taxMinor = s.taxTreatment === 'Taxable' && s.gstRateBasisPoints ? Math.round((unitMinor * s.gstRateBasisPoints) / 10_000) : 0;
  return { description: s.name, sac: s.sac, unitMinor, taxMinor, totalMinor: unitMinor + taxMinor };
}

/** A server refusal ("sac:format", "consultationKind:taken", 409 "changed") as the field it belongs to and a message key suffix. */
export type CatalogueProblem = 'required' | 'too_long' | 'format' | 'range' | 'taken' | 'past' | 'changed' | 'failed';
const codes: readonly string[] = ['required', 'too_long', 'format', 'range', 'taken', 'past'];
export function catalogueProblem(code: string | undefined): { field: string | null; code: CatalogueProblem } {
  if (code === 'changed') return { field: null, code: 'changed' };
  const [field, reason] = code?.split(':') ?? [];
  return reason && codes.includes(reason) ? { field: field ?? null, code: reason as CatalogueProblem } : { field: null, code: 'failed' };
}
