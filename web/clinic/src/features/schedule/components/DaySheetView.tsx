import { useI18n } from '@medvya/i18n';
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
          <h1 className="text-sm font-bold">{`${t('schedule.sheet.title')}, ${formatDay(sheet.date)}, ${sheet.timeZone}`}</h1>
          <h2 id={`sheet-${page.calendarId}`} className="mb-2 text-xl font-extrabold">{page.doctorName || t('schedule.doctor')}</h2>
          {page.rows.length === 0 ? <p>{t('schedule.sheet.empty')}</p> : (
            <table className="w-full border-collapse text-base">
              <thead>
                <tr className="border-b-2 border-black">
                  {(['time', 'token', 'patient', 'age', 'type', 'status'] as const).map(c => <th key={c} scope="col" className="py-1 pe-3 text-start">{t(`schedule.sheet.${c}`)}</th>)}
                </tr>
              </thead>
              <tbody>
                {page.rows.map(r => (
                  <tr key={`${r.start}-${r.firstName}`} className="border-b border-black/60">
                    <td className="py-1.5 pe-3 font-mono">{formatTime(new Date(r.start), sheet.timeZone)}</td>
                    <td className="py-1.5 pe-3 font-mono">{r.token ?? ''}</td>
                    <td className="py-1.5 pe-3 font-bold">{r.firstName}</td>
                    <td className="py-1.5 pe-3">{r.ageYears ?? ''}</td>
                    <td className="py-1.5 pe-3">{t(`visitType.${r.visitType}`)}</td>
                    <td className="py-1.5 pe-3">{t(`schedule.state.${r.state}`)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
          <p className="mt-4 border-t border-black pt-1 text-sm">{t('schedule.sheet.printedBy', { name: printedBy, when })}</p>
          <p className="text-sm font-bold">{t('schedule.sheet.warning')}</p>
        </section>
      ))}
    </div>
  );
}
