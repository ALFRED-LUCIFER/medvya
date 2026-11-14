import { useI18n } from '@medvya/i18n';
import { PageHeader, Table, TableBody, TableCell, TableHead, TableHeader, TableRow, TableState } from '@medvya/ui';
import type { DaySheet } from '../domain/daySheet';

/** Print-only day sheet (MDV-P1-050): one page per doctor in facility time, plain black on white, state in words. */
export default function DaySheetView({ sheet, printedBy }: { sheet: DaySheet; printedBy: string }) {
  const { t, formatDay, formatTime, formatDate } = useI18n();
  const printed = new Date(sheet.printedAt);
  const when = `${formatDate(printed, sheet.timeZone)} ${formatTime(printed, sheet.timeZone)}`;
  return (
    <div className="hidden bg-white text-black print:block">
      {sheet.pages.map(page => (
        <section key={page.calendarId} aria-labelledby={`sheet-${page.calendarId}`} className="break-after-page p-2 last:break-after-auto">
          {/* Wraps rather than truncates: on paper there is no hover or resize to read a cut title. */}
          <PageHeader className="[&_h1]:whitespace-normal" title={`${t('schedule.sheet.title')}, ${formatDay(sheet.date)}, ${sheet.timeZone}`} />
          <h2 id={`sheet-${page.calendarId}`} className="mb-2 text-xl font-extrabold">{page.doctorName || t('schedule.doctor')}</h2>
          <TableState state={page.rows.length === 0 ? 'empty' : 'ready'} messages={{ loading: '', error: '', empty: t('schedule.sheet.empty') }}>
            <Table label={`${t('schedule.sheet.title')}, ${page.doctorName || t('schedule.doctor')}`} className="border-collapse text-base" containerClassName="overflow-visible rounded-none border-0">
              <TableHeader className="bg-transparent">
                <TableRow className="border-b-2 border-black hover:bg-transparent">
                  {(['time', 'token', 'patient', 'age', 'type', 'status'] as const).map(c => <TableHead key={c} scope="col" className="h-auto py-1 ps-0 pe-3">{t(`schedule.sheet.${c}`)}</TableHead>)}
                </TableRow>
              </TableHeader>
              <TableBody>
                {page.rows.map(r => (
                  <TableRow key={`${r.start}-${r.firstName}`} className="border-b border-black/60 hover:bg-transparent">
                    <TableCell className="min-h-0 py-1.5 ps-0 pe-3 font-mono">{formatTime(new Date(r.start), sheet.timeZone)}</TableCell>
                    <TableCell className="min-h-0 py-1.5 ps-0 pe-3 font-mono">{r.token ?? ''}</TableCell>
                    <TableCell className="min-h-0 py-1.5 ps-0 pe-3 font-bold">{r.firstName}</TableCell>
                    <TableCell className="min-h-0 py-1.5 ps-0 pe-3">{r.ageYears ?? ''}</TableCell>
                    <TableCell className="min-h-0 py-1.5 ps-0 pe-3">{t(`visitType.${r.visitType}`)}</TableCell>
                    <TableCell className="min-h-0 py-1.5 ps-0 pe-3">{t(`schedule.state.${r.state}`)}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableState>
          <p className="mt-4 border-t border-black pt-1 text-sm">{t('schedule.sheet.printedBy', { name: printedBy, when })}</p>
          <p className="text-sm font-bold">{t('schedule.sheet.warning')}</p>
        </section>
      ))}
    </div>
  );
}
