import { Link } from 'react-router';
import { useI18n } from '@medvya/i18n';
import { Button, buttonVariants, Card, EmptyState, PageContainer, PageHeader, PageMessage, Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@medvya/ui';
import type { AttentionLine, ClinicToday } from '../domain/clinicToday';

type Props = {
  state: 'loading' | 'ready' | 'forbidden' | 'error';
  today: ClinicToday | null;
  lines: AttentionLine[];
  onRetry: () => void;
  /** Where each needs-attention line's action goes; the route passes only screens this person may open. */
  paths: { myDay: string | null; directory: string | null; waitingRoom: string | null };
};

/** The owner's Clinic today (MDV-P2-053): five counts the system can stand behind, revenue not tracked yet, and a needs-attention list. Read only. */
export default function ClinicTodayPage(p: Props) {
  const { t, formatTime } = useI18n();
  return (
    <PageContainer width="reading">
      <PageHeader title={t('clinicToday.title')}
        description={p.today && t('clinicToday.updated', { time: formatTime(new Date(p.today.now), p.today.timeZone) })} />
      <Body {...p} />
    </PageContainer>
  );
}

function Body(p: Props) {
  const { t, formatNumber } = useI18n();
  if (p.state === 'forbidden') return <PageMessage kind="denied" title={t('err.403')}>{t('clinicToday.forbidden')}</PageMessage>;
  if (p.state === 'error' || (p.state === 'ready' && !p.today))
    return <PageMessage kind="error" title={t('clinicToday.error.title')} action={<Button variant="outline" onClick={p.onRetry}>{t('schedule.retry')}</Button>}>{t('clinicToday.error.body')}</PageMessage>;
  if (!p.today) return <PageMessage kind="busy">{t('clinicToday.loading')}</PageMessage>;
  const d = p.today;
  const n = (value: number) => formatNumber(value);
  return (
    <>
      <Card title={t('clinicToday.counts')}>
        <dl className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
          <Count label={t('clinicToday.booked')} value={n(d.booked)} detail={t('clinicToday.toArrive', { count: n(d.toArrive) })} />
          <Count label={t('clinicToday.arrived')} value={n(d.arrivedWalkIn + d.arrivedBooked)} detail={t('clinicToday.arrivedSplit', { walkIn: n(d.arrivedWalkIn), booked: n(d.arrivedBooked) })} />
          <Count label={t('clinicToday.waiting')} value={n(d.waiting)}
            detail={d.longestWaitMinutes === null ? t('clinicToday.noData') : t('clinicToday.longest', { minutes: n(d.longestWaitMinutes) })} />
          <Count label={t('clinicToday.incomplete')} value={n(d.incompleteRegistrations)} />
          <Count label={t('clinicToday.revenue')} value={t('clinicToday.notTracked')} />
        </dl>
      </Card>
      <Card title={t('clinicToday.attention')}>
        {p.lines.length === 0 ? <EmptyState>{t('clinicToday.attentionEmpty')}</EmptyState> : (
          <ul className="flex flex-col divide-y">
            {p.lines.map(line => <Line key={line.kind} line={line} paths={p.paths} />)}
          </ul>
        )}
      </Card>
      <Card title={t('clinicToday.doctors')}>
        {d.doctors.length === 0 ? <EmptyState>{t('clinicToday.doctorsEmpty')}</EmptyState> : (
          <Table label={t('clinicToday.doctorsTable')} stacked>
            <TableHeader>
              <TableRow>
                <TableHead>{t('clinicToday.col.doctor')}</TableHead>
                <TableHead>{t('clinicToday.col.notes')}</TableHead>
                <TableHead>{t('clinicToday.col.rx')}</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {d.doctors.map(doc => (
                <TableRow key={doc.id}>
                  <TableCell label={t('clinicToday.col.doctor')} className="font-semibold">{doc.name ?? t('clinicToday.otherDoctor')}</TableCell>
                  <TableCell label={t('clinicToday.col.notes')}>
                    {doc.notesBeforeToday > 0 ? t('clinicToday.notesBefore', { count: n(doc.notesToSign), before: n(doc.notesBeforeToday) }) : n(doc.notesToSign)}
                  </TableCell>
                  <TableCell label={t('clinicToday.col.rx')}>{n(doc.toAuthorise)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </Card>
    </>
  );
}

function Count({ label, value, detail }: { label: string; value: string; detail?: string }) {
  return (
    <div className="flex flex-col gap-1 rounded-lg border p-4">
      <dt className="font-bold">{label}</dt>
      <dd className="text-heading">{value}</dd>
      {detail && <dd className="text-sm text-muted-foreground">{detail}</dd>}
    </div>
  );
}

function Line({ line, paths }: { line: AttentionLine; paths: Props['paths'] }) {
  const { t, formatNumber: n } = useI18n();
  const [text, action, to] = line.kind === 'mine' ? [t('clinicToday.line.mine', { notes: n(line.notes), prescriptions: n(line.prescriptions) }), t('clinicToday.action.myDay'), paths.myDay]
    : line.kind === 'incomplete' ? [t('clinicToday.line.incomplete', { count: n(line.count) }), t('clinicToday.action.list'), paths.directory]
    : [t('clinicToday.line.waiting', { count: n(line.count), minutes: n(line.minutes) }), t('clinicToday.action.waitingRoom'), paths.waitingRoom];
  return (
    <li className="flex flex-wrap items-center justify-between gap-3 py-3">
      <span className="font-semibold">{text}</span>
      {to && <Link to={to} className={buttonVariants({ variant: 'outline' })}>{action}</Link>}
    </li>
  );
}
