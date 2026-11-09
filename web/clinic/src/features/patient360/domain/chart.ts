import { refusalOf, type ClinicalRefusal } from '../../clinical-access/domain/clinicalAccess';
import type { SexAtBirth } from '../../patients/domain/directory';

// Patient 360 (MDV-P2-004): what the server answers. The server decides every access; nothing here grants anything.
export type ChartSource = 'ClinicianVerified' | 'PatientReported' | 'Imported' | 'SyncedFromHis';
export type AllergyState = 'NotRecorded' | 'NoKnownAllergies' | 'Listed';
export type AllergyStatus = 'Confirmed' | 'PatientReported' | 'Unknown' | 'NotAnAllergy';
export type ObservationCode = 'BloodPressureSystolic' | 'BloodPressureDiastolic' | 'Pulse' | 'Temperature' | 'OxygenSaturation' | 'Weight' | 'HbA1c';

export type Allergy = { substance: string; reaction: string | null; status: AllergyStatus; source: ChartSource };
export type Problem = { icd10Code: string | null; description: string; verification: 'Verified' | 'PatientReported' | 'Provisional'; onsetOn: string | null; source: ChartSource };
export type Medicine = { name: string; dose: string | null; state: 'Active' | 'Stopped'; source: ChartSource; stoppedAt: string | null };
export type Vital = { code: ObservationCode; value: number; unit: string; takenAt: string; source: ChartSource };
export type TrendPoint = { value: number; takenAt: string; source: ChartSource };
export type Trend = { code: ObservationCode; unit: string; points: TrendPoint[] };
export type RecentEncounter = { id: string; startedAt: string; status: 'Active' | 'Ended' };

export type Patient360 = {
  patient: { id: string; fullName: string; mrn: string; dateOfBirth: string; ageYears: number; sexAtBirth: SexAtBirth };
  treatmentConsent: boolean;
  /** The session facility's IANA zone: times show in it, whatever the device's zone. */
  timeZone: string;
  chart: {
    allergyState: AllergyState; allergies: Allergy[]; problems: Problem[]; medicines: Medicine[]; lastVitals: Vital[]; trends: Trend[];
    recentEncounters: RecentEncounter[]; openEncounterId: string | null;
  };
};

/** The shared provenance tag looks (@medvya/ui), by the server's source names. */
export const provenanceOf = { ClinicianVerified: 'verified', PatientReported: 'patient', Imported: 'imported', SyncedFromHis: 'his' } as const satisfies Record<ChartSource, string>;

const allergyBannerStatus = { Confirmed: 'verified', PatientReported: 'patientReported', Unknown: 'unknown' } as const;
/** The status names the chart words (patient360.allergyStatus.*), by the banner's own. */
export const allergyStatusOf = { verified: 'Confirmed', patientReported: 'PatientReported', unknown: 'Unknown' } as const;

/**
 * The banner's allergies: null = not recorded, [] = no known allergies, else the substances. "Not an allergy" entries are cleared, so they are
 * left out; if that leaves nothing the state is unclear, and it reads "not recorded", never "none" (the unsafe reading).
 */
export function bannerAllergies(chart: Pick<Patient360['chart'], 'allergyState' | 'allergies'>): { name: string; status: 'verified' | 'patientReported' | 'unknown' }[] | null {
  if (chart.allergyState === 'NotRecorded') return null;
  if (chart.allergyState === 'NoKnownAllergies') return [];
  const shown = chart.allergies.flatMap(a => (a.status === 'NotAnAllergy' ? [] : [{ name: a.substance, status: allergyBannerStatus[a.status] }]));
  return shown.length > 0 ? shown : null;
}

type Pt = { x: number; y: number };
/** One trend row: blood pressure is two series, shown as one "120/80" line. */
export type TrendLine = { key: 'HbA1c' | 'BloodPressure' | 'Weight'; unit: string; latest: string; points: Pt[]; second: Pt[]; count: number; source: ChartSource; takenAt: string };

const W = 120, H = 32, PAD = 3;
export const sparkBox = { width: W, height: H } as const;

/** Maps readings (oldest first) to points in a W x H box, y up. One reading is a single point in the middle; equal readings sit on a flat line. */
export function sparkline(values: number[], min = Math.min(...values), max = Math.max(...values)): Pt[] {
  const span = max - min;
  return values.map((v, i) => ({
    x: values.length === 1 ? W / 2 : PAD + (i * (W - 2 * PAD)) / (values.length - 1),
    y: span === 0 ? H / 2 : H - PAD - ((v - min) * (H - 2 * PAD)) / span,
  }));
}

const fmt = (n: number) => String(Number(n.toFixed(2)));

function line(key: TrendLine['key'], t: Trend, points: Pt[], second: Pt[], latest: string): TrendLine {
  const last = t.points.at(-1)!;
  return { key, unit: t.unit, latest, points, second, count: t.points.length, source: last.source, takenAt: last.takenAt };
}

/** The three trends the designs show (HbA1c, BP, weight), in that order; a measure with no readings is left out. */
export function trendLines(trends: Trend[]): TrendLine[] {
  const by = (code: ObservationCode) => trends.find(t => t.code === code && t.points.length > 0);
  const lines: TrendLine[] = [];
  const hba1c = by('HbA1c');
  if (hba1c) lines.push(line('HbA1c', hba1c, sparkline(hba1c.points.map(p => p.value)), [], fmt(hba1c.points.at(-1)!.value)));
  const sys = by('BloodPressureSystolic'), dia = by('BloodPressureDiastolic');
  if (sys) {
    // One scale for both series so the diastolic line sits below the systolic one.
    const all = [...sys.points, ...(dia?.points ?? [])].map(p => p.value);
    const [min, max] = [Math.min(...all), Math.max(...all)];
    lines.push(line('BloodPressure', sys, sparkline(sys.points.map(p => p.value), min, max), dia ? sparkline(dia.points.map(p => p.value), min, max) : [],
      dia ? `${fmt(sys.points.at(-1)!.value)}/${fmt(dia.points.at(-1)!.value)}` : fmt(sys.points.at(-1)!.value)));
  }
  const weight = by('Weight');
  if (weight) lines.push(line('Weight', weight, sparkline(weight.points.map(p => p.value)), [], fmt(weight.points.at(-1)!.value)));
  return lines;
}

/** SVG path for a polyline of points. */
export const pathOf = (pts: Pt[]) => pts.map((p, i) => `${i === 0 ? 'M' : 'L'}${p.x.toFixed(1)} ${p.y.toFixed(1)}`).join(' ');

export { refusalOf };
export type { ClinicalRefusal };
