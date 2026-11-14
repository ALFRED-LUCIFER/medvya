import type { ReactNode } from 'react';
import { Link } from 'react-router';
import { ltr, useI18n } from '@medvya/i18n';
import { Button, buttonVariants, PatientBanner, ProvenanceTag, Table, TableBody, TableCaption, TableCell, TableHead, TableHeader, TableRow } from '@medvya/ui';
import { Armchair, ArrowDown, ArrowUp, Baby, CircleAlert, CircleCheck, CircleHelp, Droplet, HeartPulse, Languages, User, Users } from 'lucide-react';
import RequestAccess from '../../clinical-access/components/RequestAccess';
import type { AccessReason } from '../../clinical-access/domain/clinicalAccess';
import type { Bmi, ChartSection, ClinicalRefusal, LifeStage, Patient360, Vital, VitalTile } from '../domain/chart';
import { bannerAlerts, bannerAllergyState, chartSections, otherVitals, pathOf, provenanceOf, resultRows, showsSection, sparkBox, vitalTiles } from '../domain/chart';

type Props = {
  data: Patient360 | null; loading: boolean; notFound: boolean; refusal: ClinicalRefusal | null; failed: boolean;
  backPath: string; consultPath: (encounterId: string) => string;
  /** A role that prints prescriptions also gets a prescribed medicine's printed prescription. */
  printRx?: boolean;
  reason: AccessReason; asking: boolean; askFailed: boolean; onReason: (r: AccessReason) => void; onRequestAccess: () => void;
  section: ChartSection; onSection: (s: ChartSection) => void;
  /** Slots the route fills from other features, so each can change without touching this layout. */
  history?: ReactNode; timeline?: ReactNode; documents?: ReactNode;
  /** The life-stage module (pregnancy, growth, immunisations, older adult review): empty until one is built for this stage. */
  stageModule?: ReactNode;
  /** "Start consultation" for the doctor's own waiting visit today (MDV-1089); without it the chart says to use the board. */
  startConsult?: ReactNode;
  /** Care gaps and goals (MDV-P2-038): renders nothing until it exists. */
  carePlan?: ReactNode;
  /** Add medicine (MDV-P2-039) on the Medicines card, for a role that may prescribe. */
  addMedicine?: ReactNode;
};

/** The life stage is said in words beside an icon, never by colour alone. */
const stageIcon = { Adult: User, Child: Baby, Pregnancy: HeartPulse, OlderAdult: Armchair } as const satisfies Record<LifeStage, unknown>;

/**
 * Patient 360 on one page (MDV-P2-004, MDV-P2-033, E-01): the banner, then section buttons over the vitals strip, problems and medicines, the
 * life-stage module, results, history and timeline, with documents and the care plan in a right-hand column from the laptop width up.
 * Every fact carries its source tag. The "AI summary · not reviewed" panel is P3 (A-01) and not rendered.
 */
export default function Patient360Page(p: Props) {
  const { t } = useI18n();
  if (p.loading) return <p role="status">{t('patient360.loading')}</p>;
  if (p.refusal) {
    return (
      <Message title={t('patient360.refused.title')} backPath={p.backPath}
        body={<RequestAccess refusal={p.refusal} roleMessage={t('patient360.refused.role')} reason={p.reason} asking={p.asking} askFailed={p.askFailed} onReason={p.onReason} onRequestAccess={p.onRequestAccess} />} />
    );
  }
  if (p.notFound || p.failed || !p.data) return <Message title={t(p.notFound ? 'patient360.notFound' : 'patient360.loadFailed')} backPath={p.backPath} />;
  const { chart, treatmentConsent, timeZone, lifeStage, patient } = p.data;
  const Stage = stageIcon[lifeStage];
  const show = (s: Exclude<ChartSection, 'all'>) => showsSection(p.section, s);
  const others = otherVitals(chart.lastVitals);
  return (
    <div className="flex flex-col gap-4">
      <h1 className="sr-only">{t('patient360.title')}</h1>
      <ChartBanner data={p.data} />
      {/* Banner details (MDV-P2-032): blood group, language and care team only when recorded; nothing is guessed. Alerts are the results out of range. */}
      <ul aria-label={t('patient360.bannerDetails')} className="flex flex-wrap items-center gap-x-5 gap-y-2 text-sm">
        <li className="inline-flex items-center gap-1.5 rounded-md border px-2 py-0.5 font-bold"><Stage aria-hidden className="size-4" />{t(`patient360.lifeStage.${lifeStage}`)}</li>
        {chart.bloodGroup && <li className="inline-flex items-center gap-1.5"><Droplet aria-hidden className="size-4" />{t('patient360.bloodGroup', { value: ltr(chart.bloodGroup) })}</li>}
        {patient.preferredLanguage && <li className="inline-flex items-center gap-1.5"><Languages aria-hidden className="size-4" />{t('patient360.language', { value: patient.preferredLanguage })}</li>}
        {chart.careTeam.length > 0 && <li className="inline-flex items-center gap-1.5"><Users aria-hidden className="size-4" />{t('patient360.careTeam', { names: chart.careTeam.join(', ') })}</li>}
        {bannerAlerts(chart.lastLabs).map(l => (
          <li key={l.code} className="inline-flex items-center gap-1.5 rounded-md border border-foreground px-2 py-0.5 font-bold">
            {l.flag === 'High' ? <ArrowUp aria-hidden className="size-4" /> : <ArrowDown aria-hidden className="size-4" />}
            {t(`patient360.alert.${l.flag!}`, { name: t(`patient360.code.${l.code}`), value: ltr(`${l.value} ${l.unit}`) })}
          </li>))}
      </ul>
      <div className="flex flex-wrap items-center gap-x-5 gap-y-2 text-sm">
        <span className="inline-flex items-center gap-1.5">
          {treatmentConsent ? <><CircleCheck aria-hidden className="size-4" />{t('consult.banner.consentGiven')}</> : <><CircleAlert aria-hidden className="size-4" />{t('consult.banner.consentNotRecorded')}</>}
        </span>
        {chart.openEncounterId
          ? <Link to={p.consultPath(chart.openEncounterId)} className={buttonVariants({ variant: 'default' })}>{t('consult.open')}</Link>
          : p.startConsult ?? <span className="text-muted-foreground">{t('patient360.startFromBoard')}</span>}
        <Link to={p.backPath} className={buttonVariants({ variant: 'outline' })}>{t('patient360.back')}</Link>
      </div>
      <div role="group" aria-label={t('patient360.section.label')} className="sticky top-0 z-10 flex flex-wrap gap-2 bg-background py-1">
        {chartSections.map(s => (
          <Button key={s} variant={s === p.section ? 'selected' : 'outline'} aria-pressed={s === p.section} onClick={() => p.onSection(s)}>
            {t(s === 'stage' ? `patient360.section.stage.${lifeStage}` : `patient360.section.${s}`)}
          </Button>))}
      </div>
      {show('trends') && (
        <section aria-label={t('patient360.vitals')} className="flex flex-col gap-3">
          <h2 className="text-base font-extrabold">{t('patient360.vitals')}</h2>
          <ul className="grid grid-cols-[repeat(auto-fit,minmax(min(100%,10rem),1fr))] gap-3">
            {vitalTiles(chart).map(tile => <Tile key={tile.key} tile={tile} zone={timeZone} />)}
            <BmiTile bmi={chart.bmi} zone={timeZone} />
          </ul>
          {others.length > 0 && <Readings items={others} zone={timeZone} />}
          {chart.lastVitals.some(v => v.range) && <p className="text-sm text-muted-foreground">{t('patient360.rangesDraft')}</p>}
        </section>
      )}
      <div className="grid items-start gap-4 lg:grid-cols-[minmax(0,1fr)_18rem]">
        <div className="flex min-w-0 flex-col gap-4">
          {show('summary') && (
            <div className="grid gap-4 md:grid-cols-2">
              <Card title={t('patient360.allergies')} wide>
                {chart.allergyState === 'Listed'
                  ? <List>{chart.allergies.map((a, i) => (
                      <Item key={i} source={a.source}>
                        <strong>{a.substance}</strong>
                        <span>{[a.reaction, t(`patient360.allergyStatus.${a.status}`)].filter(Boolean).join(' · ')}</span>
                      </Item>))}</List>
                  : <p className="inline-flex items-center gap-1.5 font-bold">{t(chart.allergyState === 'NoKnownAllergies' ? 'banner.noAllergies' : 'banner.allergiesNotRecorded')}</p>}
              </Card>
              <Card title={t('patient360.problems')}>
                {chart.problems.length === 0 ? <Empty text={t('patient360.problems.empty')} />
                  : <List>{chart.problems.map((x, i) => (
                      <Item key={i} source={x.source}>
                        <strong>{x.description}</strong>
                        <span className={x.verification === 'Provisional' ? 'inline-flex items-center gap-1.5 self-start rounded-md border border-dashed border-foreground px-2 py-0.5' : undefined}>
                          {x.verification === 'Provisional' && <CircleHelp aria-hidden className="size-4 shrink-0" />}
                          {[x.icd10Code && ltr(x.icd10Code), t(`patient360.verification.${x.verification}`), x.status === 'Resolved' && t('problems.status.Resolved')].filter(Boolean).join(' · ')}
                        </span>
                      </Item>))}</List>}
              </Card>
              <Card title={t('patient360.medicines')}>
                {p.addMedicine}
                {chart.medicines.length === 0 ? <Empty text={t('patient360.medicines.empty')} />
                  : <List>{chart.medicines.map((m, i) => (
                      <Item key={i} source={m.source}>
                        <strong>{m.name}</strong>
                        <span>{[m.dose && ltr(m.dose), t(`patient360.medicineState.${m.state}`)].filter(Boolean).join(' · ')}{m.stoppedAt && <>{' · '}<DateText iso={m.stoppedAt} zone={timeZone} /></>}</span>
                        {m.encounterId && <PrescribedLinks encounterId={m.encounterId} consultPath={p.consultPath} printRx={!!p.printRx} />}
                      </Item>))}</List>}
              </Card>
            </div>
          )}
          {/* Visits sit right under the summary (UX-09): the most used part of the chart is not at the bottom. */}
          {show('timeline') && p.timeline}
          {show('stage') && (p.stageModule ?? (
            <Card title={t(`patient360.section.stage.${lifeStage}`)}><Empty text={t('patient360.stage.empty')} /></Card>
          ))}
          {show('trends') && <Results chart={chart} zone={timeZone} />}
          {show('history') && p.history}
        </div>
        <aside aria-label={t('patient360.aside')} className="flex min-w-0 flex-col gap-4">
          {p.carePlan}
          {p.documents}
        </aside>
      </div>
    </div>
  );
}

/** The identity banner with the allergy state, shared by every Patient 360 screen (chart, timeline): PatientBanner full (MDV-P1-092). */
export function ChartBanner({ data: { patient, chart } }: { data: Patient360 }) {
  const { t, formatCalendarDate } = useI18n();
  return (
    <PatientBanner variant="full" patient={{ name: patient.fullName, age: patient.ageYears, sex: patient.sexAtBirth, mrn: patient.mrn, dob: formatCalendarDate(patient.dateOfBirth),
      allergy: bannerAllergyState(chart, s => t(`patient360.allergyStatus.${s}`)) }} />
  );
}

function Card({ title, wide, children }: { title: string; wide?: boolean; children: ReactNode }) {
  return (
    <section aria-label={title} className={`flex flex-col gap-2 rounded-xl border bg-card p-4 ${wide ? 'md:col-span-2' : ''}`}>
      <h2 className="text-base font-extrabold">{title}</h2>
      {children}
    </section>
  );
}

const List = ({ children }: { children: ReactNode }) => <ul className="flex flex-col gap-2">{children}</ul>;
const Empty = ({ text }: { text: string }) => <p className="text-muted-foreground">{text}</p>;

/** A chart fact with its source tag: no value is shown without one. */
/** Where a prescribed medicine came from (MDV-1087): its visit and, for a role that prints prescriptions, the printed prescription. */
export function PrescribedLinks({ encounterId, consultPath, printRx }: { encounterId: string; consultPath: (encounterId: string) => string; printRx: boolean }) {
  const { t } = useI18n();
  const link = buttonVariants({ variant: 'link', className: 'px-0' });
  return (
    <span className="flex flex-wrap gap-x-4">
      <Link to={consultPath(encounterId)} className={link}>{t('medicine.prescribed.visit')}</Link>
      {printRx && <Link to={`${consultPath(encounterId)}/rx/print`} className={link}>{t('medicine.prescribed.print')}</Link>}
    </span>
  );
}

function Item({ source, children }: { source: keyof typeof provenanceOf; children: ReactNode }) {
  return (
    <li className="flex flex-wrap items-center justify-between gap-x-3 gap-y-1">
      <span className="flex flex-col">{children}</span>
      <ProvenanceTag source={provenanceOf[source]} />
    </li>
  );
}

/** Latest readings the strip has no tile for, each flagged in words with an arrow (never colour alone) against its range. */
function Readings({ items, zone }: { items: Vital[]; zone: string }) {
  const { t } = useI18n();
  return (
    <List>
      {items.map(v => (
        <Item key={v.code} source={v.source}>
          <strong>{t(`patient360.code.${v.code}`)}</strong>
          <span>{ltr(`${v.value} ${v.unit}`)}{' · '}<DateText iso={v.takenAt} zone={zone} /></span>
          {v.flag && v.range && <Flag flag={v.flag} text={t(`patient360.flag.${v.flag}`, { range: ltr(`${v.range} ${v.unit}`) })} />}
        </Item>))}
    </List>
  );
}

const Flag = ({ flag, text }: { flag: 'High' | 'Low'; text: string }) => (
  <span className="inline-flex items-center gap-1.5 font-bold">
    {flag === 'High' ? <ArrowUp aria-hidden className="size-4 shrink-0" /> : <ArrowDown aria-hidden className="size-4 shrink-0" />}{text}
  </span>
);

type Pts = { x: number; y: number }[];
function Spark({ label, points, second }: { label: string; points: Pts; second?: Pts }) {
  return (
    <svg role="img" aria-label={label} viewBox={`0 0 ${sparkBox.width} ${sparkBox.height}`} className="h-8 w-full max-w-32 text-brand-700" fill="none">
      {second && second.length > 0 && <path d={pathOf(second)} stroke="currentColor" strokeWidth="1.5" strokeDasharray="3 2" />}
      <path d={pathOf(points)} stroke="currentColor" strokeWidth="2" />
      {points.length === 1 && <circle cx={points[0]!.x} cy={points[0]!.y} r="2.5" fill="currentColor" />}
    </svg>
  );
}

/** One vitals tile: the latest value, its trend line and its flag in words; "not recorded" when never taken. */
function Tile({ tile: { key, reading: r }, zone }: { tile: VitalTile; zone: string }) {
  const { t, formatDate } = useI18n();
  const name = t(`patient360.tile.${key}`);
  if (!r) return <li className="flex flex-col gap-1 rounded-xl border bg-card p-3"><strong>{name}</strong><span className="text-muted-foreground">{t('patient360.tile.none')}</span></li>;
  const value = ltr(`${r.value} ${r.unit}`), date = formatDate(new Date(r.takenAt), zone);
  return (
    <li className="flex flex-col gap-1 rounded-xl border bg-card p-3">
      <strong>{name}</strong>
      <span className="text-lg font-extrabold">{value}</span>
      <Spark label={t('patient360.trend.alt', { name, value, date, count: r.count })} points={r.points} second={r.second} />
      {r.flags.map(f => <Flag key={f.code} flag={f.flag} text={`${r.flags.length > 1 ? `${t(`patient360.code.${f.code}`)}: ` : ''}${t(`patient360.flag.${f.flag}`, { range: ltr(`${f.range} ${r.unit}`) })}`} />)}
      <span className="text-sm text-muted-foreground">{t('patient360.trend.latest', { date })}</span>
      <ProvenanceTag source={provenanceOf[r.source]} />
    </li>
  );
}

/** BMI, worked out by the server from the latest weight and height; it names the older of the two. */
function BmiTile({ bmi, zone }: { bmi: Bmi | null; zone: string }) {
  const { t, formatDate } = useI18n();
  return (
    <li className="flex flex-col gap-1 rounded-xl border bg-card p-3">
      <strong>{t('patient360.tile.Bmi')}</strong>
      {bmi ? <>
        <span className="text-lg font-extrabold">{ltr(String(bmi.value))}</span>
        <span className="text-sm text-muted-foreground">{t(`patient360.bmi.older.${bmi.olderCode}`, { date: formatDate(new Date(bmi.olderAt), zone) })}</span>
      </> : <span className="text-muted-foreground">{t('patient360.tile.none')}</span>}
    </li>
  );
}

/** Results and trends: each latest lab with its reference range, trend line, flag in words and source tag. */
function Results({ chart, zone }: { chart: Patient360['chart']; zone: string }) {
  const { t, formatDate } = useI18n();
  const rows = resultRows(chart);
  return (
    <Card title={t('patient360.labs')}>
      {rows.length === 0 ? <Empty text={t('patient360.labs.empty')} /> : <>
        <Table label={t('patient360.labs.table')}>
          <TableCaption className="sr-only">{t('patient360.labs')}</TableCaption>
          <TableHeader>
            <TableRow>
              <TableHead scope="col">{t('patient360.labs.test')}</TableHead>
              <TableHead scope="col">{t('patient360.labs.latest')}</TableHead>
              <TableHead scope="col">{t('patient360.labs.range')}</TableHead>
              <TableHead scope="col">{t('patient360.labs.trend')}</TableHead>
              <TableHead scope="col">{t('patient360.labs.flag')}</TableHead>
              <TableHead scope="col">{t('timeline.source')}</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {rows.map(({ lab: l, points, count }) => {
              const name = t(`patient360.code.${l.code}`), value = ltr(`${l.value} ${l.unit}`), date = formatDate(new Date(l.takenAt), zone);
              return (
                <TableRow key={l.code}>
                  <TableCell><span className="flex flex-col"><strong>{name}</strong><time dateTime={l.takenAt} className="text-sm text-muted-foreground">{date}</time></span></TableCell>
                  <TableCell className="font-bold">{value}</TableCell>
                  <TableCell>{l.range ? ltr(`${l.range} ${l.unit}`) : t('patient360.labs.noRange')}</TableCell>
                  <TableCell><Spark label={t('patient360.trend.alt', { name, value, date, count })} points={points} /></TableCell>
                  <TableCell>{l.flag && l.range ? <Flag flag={l.flag} text={t(`patient360.flag.${l.flag}`, { range: ltr(`${l.range} ${l.unit}`) })} /> : l.range && t('patient360.labs.inRange')}</TableCell>
                  <TableCell><ProvenanceTag source={provenanceOf[l.source]} /></TableCell>
                </TableRow>);
            })}
          </TableBody>
        </Table>
        {rows.some(r => r.lab.range) && <p className="text-sm text-muted-foreground">{t('patient360.rangesDraft')}</p>}
      </>}
    </Card>
  );
}

function DateText({ iso, zone }: { iso: string; zone: string }) {
  const { formatDate } = useI18n();
  return <time dateTime={iso}>{formatDate(new Date(iso), zone)}</time>;
}

function Message({ title, body, backPath }: { title: string; body?: ReactNode; backPath: string }) {
  const { t } = useI18n();
  return (
    <div role="alert" className="flex flex-col items-start gap-3 rounded-xl border bg-card p-6">
      <h1 className="text-lg font-extrabold">{title}</h1>
      {body && <div className="flex flex-col items-start gap-2">{body}</div>}
      <Link to={backPath} className={buttonVariants({ variant: 'outline' })}>{t('patient360.back')}</Link>
    </div>
  );
}
