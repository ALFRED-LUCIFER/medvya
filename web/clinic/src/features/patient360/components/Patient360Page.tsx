import type { ReactNode } from 'react';
import { Link } from 'react-router';
import { ltr, useI18n } from '@medvya/i18n';
import { buttonVariants, IdentityBanner, ProvenanceTag } from '@medvya/ui';
import { Armchair, ArrowDown, ArrowUp, Baby, CircleAlert, CircleCheck, CircleHelp, Droplet, HeartPulse, Languages, User, Users } from 'lucide-react';
import RequestAccess from '../../clinical-access/components/RequestAccess';
import type { AccessReason } from '../../clinical-access/domain/clinicalAccess';
import type { Bmi, ClinicalRefusal, LifeStage, Patient360, TrendLine, Vital } from '../domain/chart';
import { bannerAllergies, pathOf, provenanceOf, sparkBox, trendLines } from '../domain/chart';

type Props = {
  data: Patient360 | null; loading: boolean; notFound: boolean; refusal: ClinicalRefusal | null; failed: boolean;
  backPath: string; consultPath: (encounterId: string) => string; timelinePath: string; documentsPath: string;
  reason: AccessReason; asking: boolean; askFailed: boolean; onReason: (r: AccessReason) => void; onRequestAccess: () => void;
};

const sex = { Female: 'F', Male: 'M', Other: 'X' } as const;
/** The life stage is said in words beside an icon, never by colour alone. */
const stageIcon = { Adult: User, Child: Baby, Pregnancy: HeartPulse, OlderAdult: Armchair } as const satisfies Record<LifeStage, unknown>;

/**
 * Patient 360 (MDV-P2-004, E-01): the identity banner with the allergy state, then the verified chart. Every fact carries its source tag.
 * The right-hand "AI summary · not reviewed" panel is reserved for P3 (A-01) and not rendered: there is no flag to turn on yet.
 * Two columns from the tablet width up, one below it.
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
  const trends = trendLines(chart.trends);
  const Stage = stageIcon[lifeStage];
  return (
    <div className="flex flex-col gap-4">
      <h1 className="sr-only">{t('patient360.title')}</h1>
      <PatientBanner data={p.data} />
      {/* Banner details (MDV-P2-032): blood group, language and care team only when recorded; nothing is guessed. */}
      <ul aria-label={t('patient360.bannerDetails')} className="flex flex-wrap items-center gap-x-5 gap-y-2 text-sm">
        <li className="inline-flex items-center gap-1.5 rounded-md border px-2 py-0.5 font-bold"><Stage aria-hidden className="size-4" />{t(`patient360.lifeStage.${lifeStage}`)}</li>
        {chart.bloodGroup && <li className="inline-flex items-center gap-1.5"><Droplet aria-hidden className="size-4" />{t('patient360.bloodGroup', { value: ltr(chart.bloodGroup) })}</li>}
        {patient.preferredLanguage && <li className="inline-flex items-center gap-1.5"><Languages aria-hidden className="size-4" />{t('patient360.language', { value: patient.preferredLanguage })}</li>}
        {chart.careTeam.length > 0 && <li className="inline-flex items-center gap-1.5"><Users aria-hidden className="size-4" />{t('patient360.careTeam', { names: chart.careTeam.join(', ') })}</li>}
      </ul>
      <div className="flex flex-wrap items-center gap-x-5 gap-y-2 text-sm">
        <span className="inline-flex items-center gap-1.5">
          {treatmentConsent ? <><CircleCheck aria-hidden className="size-4" />{t('consult.banner.consentGiven')}</> : <><CircleAlert aria-hidden className="size-4" />{t('consult.banner.consentNotRecorded')}</>}
        </span>
        {chart.openEncounterId
          ? <Link to={p.consultPath(chart.openEncounterId)} className={buttonVariants({ variant: 'navy', className: 'min-h-11' })}>{t('consult.open')}</Link>
          : <span className="text-muted-foreground">{t('patient360.startFromBoard')}</span>}
        <Link to={p.documentsPath} className={buttonVariants({ variant: 'outline', className: 'min-h-11' })}>{t('documents.title')}</Link>
        <Link to={p.timelinePath} className={buttonVariants({ variant: 'outline', className: 'min-h-11' })}>{t('timeline.open')}</Link>
        <Link to={p.backPath} className={buttonVariants({ variant: 'outline', className: 'min-h-11' })}>{t('patient360.back')}</Link>
      </div>
      <div className="grid gap-4 md:grid-cols-2">
        <Card title={t('patient360.allergies')}>
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
          {chart.medicines.length === 0 ? <Empty text={t('patient360.medicines.empty')} />
            : <List>{chart.medicines.map((m, i) => (
                <Item key={i} source={m.source}>
                  <strong>{m.name}</strong>
                  <span>{[m.dose && ltr(m.dose), t(`patient360.medicineState.${m.state}`)].filter(Boolean).join(' · ')}{m.stoppedAt && <>{' · '}<DateText iso={m.stoppedAt} zone={timeZone} /></>}</span>
                </Item>))}</List>}
        </Card>
        <Card title={t('patient360.vitals')}>
          {chart.lastVitals.length === 0 ? <Empty text={t('patient360.vitals.empty')} /> : <Readings items={chart.lastVitals} bmi={chart.bmi} zone={timeZone} />}
        </Card>
        <Card title={t('patient360.labs')}>
          {chart.lastLabs.length === 0 ? <Empty text={t('patient360.labs.empty')} /> : <Readings items={chart.lastLabs} zone={timeZone} />}
        </Card>
        <Card title={t('patient360.trends')} wide>
          {trends.length === 0 ? <Empty text={t('patient360.trends.empty')} />
            : <ul className="grid gap-4 sm:grid-cols-3">{trends.map(l => <TrendCard key={l.key} line={l} zone={timeZone} />)}</ul>}
        </Card>
        <Card title={t('patient360.encounters')} wide>
          {chart.recentEncounters.length === 0 ? <Empty text={t('patient360.encounters.empty')} />
            : <List>{chart.recentEncounters.map(e => (
                <li key={e.id} className="flex flex-wrap items-center gap-x-3 gap-y-1">
                  <DateText iso={e.startedAt} zone={timeZone} /><span>{t(`patient360.encounterStatus.${e.status}`)}</span>
                </li>))}</List>}
        </Card>
      </div>
    </div>
  );
}

/** The identity banner with the allergy state, shared by every Patient 360 screen (chart, timeline, documents). */
export function PatientBanner({ data: { patient, chart } }: { data: Patient360 }) {
  return (
    <IdentityBanner patient={{ name: patient.fullName, age: patient.ageYears, sex: sex[patient.sexAtBirth], mrn: patient.mrn, dob: patient.dateOfBirth, allergies: bannerAllergies(chart) }} />
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
function Item({ source, children }: { source: keyof typeof provenanceOf; children: ReactNode }) {
  return (
    <li className="flex flex-wrap items-center justify-between gap-x-3 gap-y-1">
      <span className="flex flex-col">{children}</span>
      <ProvenanceTag source={provenanceOf[source]} />
    </li>
  );
}

/** Latest readings, each flagged in words with an arrow (never colour alone) against its adult range, and BMI when it can be worked out. */
function Readings({ items, bmi, zone }: { items: Vital[]; bmi?: Bmi | null; zone: string }) {
  const { t, formatDate } = useI18n();
  return (
    <>
      <List>
        {items.map(v => (
          <Item key={v.code} source={v.source}>
            <strong>{t(`patient360.code.${v.code}`)}</strong>
            <span>{ltr(`${v.value} ${v.unit}`)}{' · '}<DateText iso={v.takenAt} zone={zone} /></span>
            {v.flag && v.range && (
              <span className="inline-flex items-center gap-1.5 font-bold">
                {v.flag === 'High' ? <ArrowUp aria-hidden className="size-4 shrink-0" /> : <ArrowDown aria-hidden className="size-4 shrink-0" />}
                {t(`patient360.flag.${v.flag}`, { range: ltr(`${v.range} ${v.unit}`) })}
              </span>
            )}
          </Item>))}
        {bmi && (
          <li className="flex flex-col">
            <strong>{t('patient360.bmi', { value: ltr(String(bmi.value)) })}</strong>
            <span className="text-muted-foreground">{t(`patient360.bmi.older.${bmi.olderCode}`, { date: formatDate(new Date(bmi.olderAt), zone) })}</span>
          </li>
        )}
      </List>
      {items.some(v => v.range) && <p className="text-sm text-muted-foreground">{t('patient360.rangesDraft')}</p>}
    </>
  );
}

function DateText({ iso, zone }: { iso: string; zone: string }) {
  const { formatDate } = useI18n();
  return <time dateTime={iso}>{formatDate(new Date(iso), zone)}</time>;
}

function TrendCard({ line, zone }: { line: TrendLine; zone: string }) {
  const { t, formatDate } = useI18n();
  const name = t(`patient360.trend.${line.key}`);
  const value = ltr(`${line.latest} ${line.unit}`);
  const date = formatDate(new Date(line.takenAt), zone);
  return (
    <li className="flex flex-col gap-1">
      <strong>{name}</strong>
      <span className="text-lg font-extrabold">{value}</span>
      <svg role="img" aria-label={t('patient360.trend.alt', { name, value, date, count: line.count })} viewBox={`0 0 ${sparkBox.width} ${sparkBox.height}`} className="h-8 w-full max-w-40 text-brand-700" fill="none">
        {line.second.length > 0 && <path d={pathOf(line.second)} stroke="currentColor" strokeWidth="1.5" strokeDasharray="3 2" />}
        <path d={pathOf(line.points)} stroke="currentColor" strokeWidth="2" />
        {line.points.length === 1 && <circle cx={line.points[0]!.x} cy={line.points[0]!.y} r="2.5" fill="currentColor" />}
      </svg>
      <span className="text-sm text-muted-foreground">{t('patient360.trend.latest', { date })}</span>
      <ProvenanceTag source={provenanceOf[line.source]} />
    </li>
  );
}

function Message({ title, body, backPath }: { title: string; body?: ReactNode; backPath: string }) {
  const { t } = useI18n();
  return (
    <div role="alert" className="flex flex-col items-start gap-3 rounded-xl border bg-card p-6">
      <h1 className="text-lg font-extrabold">{title}</h1>
      {body && <div className="flex flex-col items-start gap-2">{body}</div>}
      <Link to={backPath} className={buttonVariants({ variant: 'outline', className: 'min-h-11' })}>{t('patient360.back')}</Link>
    </div>
  );
}
