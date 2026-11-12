import { refusalOf, type ClinicalRefusal } from '../../clinical-access/domain/clinicalAccess';
import type { SexAtBirth } from '../../patients/domain/directory';

// Patient 360 (MDV-P2-004): what the server answers. The server decides every access; nothing here grants anything.
export type ChartSource = 'ClinicianVerified' | 'PatientReported' | 'Imported' | 'SyncedFromHis';
export type AllergyState = 'NotRecorded' | 'NoKnownAllergies' | 'Listed';
export type AllergyStatus = 'Confirmed' | 'PatientReported' | 'Unknown' | 'NotAnAllergy';
export type ObservationCode =
  | 'BloodPressureSystolic' | 'BloodPressureDiastolic' | 'Pulse' | 'Temperature' | 'OxygenSaturation' | 'Weight' | 'RespiratoryRate' | 'Height' | 'PainScore'
  | 'BloodPressureStandingSystolic' | 'BloodPressureStandingDiastolic' | 'FundalHeight' | 'FetalHeartRate' | 'Length' | 'HeadCircumference'
  | 'HbA1c' | 'LdlCholesterol' | 'Egfr' | 'UrineAlbuminCreatinineRatio' | 'Haemoglobin' | 'Ferritin' | 'Potassium' | 'Sodium' | 'Tsh' | 'VitaminD' | 'Platelets';

export type Allergy = { substance: string; reaction: string | null; status: AllergyStatus; source: ChartSource };
export type Problem = { icd10Code: string | null; description: string; verification: 'Verified' | 'PatientReported' | 'Provisional'; onsetOn: string | null; source: ChartSource; status: 'Active' | 'Resolved' };
/** `encounterId`: the visit whose authorised prescription put it on the chart (MDV-1087). */
export type Medicine = { name: string; dose: string | null; state: 'Active' | 'Stopped'; source: ChartSource; stoppedAt: string | null; encounterId?: string | null };
/** The latest reading of a vital or a lab; flag and range come from the server's adult reference range (null when the code has none). */
export type Vital = { code: ObservationCode; value: number; unit: string; takenAt: string; source: ChartSource; flag: 'High' | 'Low' | null; range: string | null };
/** BMI worked out by the server from the latest weight and height; the older of the two is named with its date. */
export type Bmi = { value: number; olderCode: 'Weight' | 'Height'; olderAt: string };
export type TrendPoint = { value: number; takenAt: string; source: ChartSource };
export type Trend = { code: ObservationCode; unit: string; points: TrendPoint[] };
export type RecentEncounter = { id: string; startedAt: string; status: 'Active' | 'Ended' };

/** The life stage the server works out from the date of birth (MDV-P2-032); shown with an icon and words. */
export type LifeStage = 'Adult' | 'Child' | 'Pregnancy' | 'OlderAdult';
export type HistorySection = 'PastMedical' | 'Surgical' | 'Family' | 'Social' | 'WomensHealth' | 'Obstetric' | 'Birth' | 'Feeding' | 'Function' | 'Falls' | 'BloodGroup';
/** One saved version of a history section; lists are newest first. */
export type HistoryEntry = { section: HistorySection; text: string; author: string | null; recordedAt: string };

/** The pregnancy record (MDV-P2-034), as the server works it out on the facility's date. */
export type ContactState = 'Done' | 'Today' | 'ToCome' | 'Missed';
export type DangerSign = 'VaginalBleeding' | 'SevereHeadacheOrBlurredVision' | 'SwellingOfFaceOrHands' | 'LeakingFluid' | 'FewerBabyMovements' | 'FeverOrBurningUrine';
export type PregnancyOutcome = 'LiveBirth' | 'Stillbirth' | 'Miscarriage' | 'Termination' | 'Ectopic' | 'EnteredInError';
export type OpenPregnancy = {
  id: string; lmp: string | null; scanDate: string | null; scanGestationDays: number | null; dueDate: string; datedBy: 'Lmp' | 'Scan';
  datingReason: 'LmpOnly' | 'ScanOnly' | 'ScanAgrees' | 'ScanDiffers'; previousDueDate: string | null; gestationDays: number;
  gravida: number | null; para: number | null; abortions: number | null; living: number | null; startedBy: string | null; startedAt: string;
  contacts: { week: number; state: ContactState; encounterId: string | null }[]; dangerSigns: DangerSign[];
  dangerSignsCounselledBy: string | null; dangerSignsCounselledAt: string | null; rulesSignedOff: boolean;
};
/** Neither: not recorded, never "not pregnant". */
export type PregnancyStatus = { open: OpenPregnancy | null; ruledOut: { recordedBy: string | null; recordedAt: string } | null };

export type Patient360 = {
  patient: { id: string; fullName: string; mrn: string; dateOfBirth: string; ageYears: number; sexAtBirth: SexAtBirth; preferredLanguage: string | null };
  lifeStage: LifeStage;
  /** The sections the chart offers at this life stage, in order (the server decides). */
  historySections: HistorySection[];
  pregnancy: PregnancyStatus;
  treatmentConsent: boolean;
  /** The session facility's IANA zone: times show in it, whatever the device's zone. */
  timeZone: string;
  chart: {
    allergyState: AllergyState; allergies: Allergy[]; problems: Problem[]; medicines: Medicine[]; lastVitals: Vital[]; trends: Trend[];
    recentEncounters: RecentEncounter[]; openEncounterId: string | null; startableAppointmentId: string | null; lastLabs: Vital[]; bmi: Bmi | null;
    history: HistoryEntry[]; bloodGroup: string | null; careTeam: string[];
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

/** The vitals strip (MDV-P2-033): five measured tiles in the design's order; BMI is the sixth, worked out by the server. */
export const tileKeys = ['BloodPressure', 'Pulse', 'OxygenSaturation', 'Temperature', 'Weight'] as const;
export type TileKey = (typeof tileKeys)[number];
const tileCodes = { BloodPressure: ['BloodPressureSystolic', 'BloodPressureDiastolic'], Pulse: ['Pulse'], OxygenSaturation: ['OxygenSaturation'], Temperature: ['Temperature'], Weight: ['Weight'] } as const satisfies Record<TileKey, readonly ObservationCode[]>;
export type TileFlag = { code: ObservationCode; flag: 'High' | 'Low'; range: string };
/** One tile: null reading when the measure was never taken. Blood pressure is one "120/80" value with both series on one scale. */
export type VitalTile = { key: TileKey; reading: { value: string; unit: string; takenAt: string; source: ChartSource; points: Pt[]; second: Pt[]; count: number; flags: TileFlag[] } | null };

const seriesOf = (chart: Pick<Patient360['chart'], 'trends'>, code: ObservationCode) => chart.trends.find(t => t.code === code)?.points.map(p => p.value) ?? [];
const flagOf = (v: Vital): TileFlag[] => (v.flag && v.range ? [{ code: v.code, flag: v.flag, range: v.range }] : []);

export function vitalTiles(chart: Pick<Patient360['chart'], 'lastVitals' | 'trends'>): VitalTile[] {
  return tileKeys.map(key => {
    const [main, other] = tileCodes[key].map(c => chart.lastVitals.find(v => v.code === c));
    if (!main) return { key, reading: null };
    const a = seriesOf(chart, main.code), b = other ? seriesOf(chart, other.code) : [];
    const all = [...a, ...b];
    const [min, max] = all.length > 0 ? [Math.min(...all), Math.max(...all)] : [main.value, main.value];
    return { key, reading: {
      value: other ? `${fmt(main.value)}/${fmt(other.value)}` : fmt(main.value), unit: main.unit, takenAt: main.takenAt, source: main.source,
      points: sparkline(a.length > 0 ? a : [main.value], min, max), second: b.length > 0 ? sparkline(b, min, max) : [], count: Math.max(a.length, 1),
      flags: [main, other].flatMap(v => (v ? flagOf(v) : [])),
    } };
  });
}

/** The latest vitals the strip has no tile for (respiratory rate, height, pain, standing BP, pregnancy measures): listed below it, never dropped. */
export const otherVitals = (vitals: Vital[]) => vitals.filter(v => !Object.values(tileCodes).some(codes => (codes as readonly ObservationCode[]).includes(v.code)));

/** The results table: each latest lab with its own trend line (oldest first). */
export const resultRows = (chart: Pick<Patient360['chart'], 'lastLabs' | 'trends'>) =>
  chart.lastLabs.map(lab => { const s = seriesOf(chart, lab.code); return { lab, points: sparkline(s.length > 0 ? s : [lab.value]), count: Math.max(s.length, 1) }; });

/** Banner alerts: the results outside their reference range, said in words. */
export const bannerAlerts = (labs: Vital[]) => labs.filter(l => l.flag && l.range);

/** The section buttons (MDV-P2-033): everything, or one part of the page. The right-hand column always shows. */
export const chartSections = ['all', 'summary', 'trends', 'stage', 'history', 'timeline'] as const;
export type ChartSection = (typeof chartSections)[number];
export const showsSection = (chosen: ChartSection, section: Exclude<ChartSection, 'all'>) => chosen === 'all' || chosen === section;
export const parseSection = (raw: string | null): ChartSection => chartSections.find(s => s === raw) ?? 'all';

/** SVG path for a polyline of points. */
export const pathOf = (pts: Pt[]) => pts.map((p, i) => `${i === 0 ? 'M' : 'L'}${p.x.toFixed(1)} ${p.y.toFixed(1)}`).join(' ');

export { refusalOf };
export type { ClinicalRefusal };
