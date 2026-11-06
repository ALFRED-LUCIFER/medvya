import { useI18n } from '@medvya/i18n';
import { Button, Input, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Switch } from '@medvya/ui';
import { slotLengths, weekdays, type CalendarTemplate, type HoursRow, type LocationChoice, type SessionRow, type Weekday } from '../domain/settings';
import { fieldError } from './fieldStyles';
import { Field } from './Field';
import { LocationSelect } from './LocationSelect';
import { Section } from './Section';

type OpeningHours = {
  rows: HoursRow[];
  problems: Weekday[];
  loading: boolean;
  loadFailed: boolean;
  dirty: boolean;
  saving: boolean;
  saved: boolean;
  saveFailed: boolean;
  onToggle: (weekday: Weekday, open: boolean) => void;
  onTime: (weekday: Weekday, field: 'opens' | 'closes', value: string) => void;
  onSave: () => void;
};

type Schedules = {
  calendars: CalendarTemplate[];
  calendarId: string;
  loading: boolean;
  loadFailed: boolean;
  slotMinutes: number;
  sessions: SessionRow[];
  problem: 'order' | 'overlap' | null;
  dirty: boolean;
  saving: boolean;
  saveFailed: boolean;
  saved: boolean;
  outside: { id: string; reference: string }[];
  onCalendar: (id: string) => void;
  onSlot: (minutes: number) => void;
  onSession: (index: number, patch: Partial<SessionRow>) => void;
  onAddSession: () => void;
  onRemoveSession: (index: number) => void;
  onSave: () => void;
};

type Props = { choice: LocationChoice; hours: OpeningHours; schedules: Schedules };

/** Hours (F-08): when each location is open, and the weekly sessions and slot length of each doctor or room. */
export default function HoursPanel({ choice, hours, schedules }: Props) {
  const { t } = useI18n();
  if (choice.locationsLoading) return <p role="status">{t('settings.loading')}</p>;
  if (choice.locationsFailed) return <p role="alert" className={fieldError}>{t('settings.loadFailed')}</p>;
  return (
    <div className="flex flex-col gap-4">
      <LocationSelect id="hours-location" locations={choice.locations} value={choice.locationId} onChange={choice.onLocation} />
      <Section title={t('settings.hours.title')} intro={t('settings.hours.intro')}>
        {hours.loading ? <p role="status">{t('settings.loading')}</p> : hours.loadFailed ? <p role="alert" className={fieldError}>{t('settings.loadFailed')}</p> : (
          <ul className="flex flex-col divide-y rounded-lg border">
            {hours.rows.map(r => {
              const bad = hours.problems.includes(r.weekday);
              return (
                <li key={r.weekday} className="flex flex-wrap items-center gap-3 p-3">
                  <label className="flex min-h-11 w-48 items-center gap-3 font-bold">
                    <Switch checked={r.open} onCheckedChange={on => hours.onToggle(r.weekday, on)} />
                    <span>{t(`settings.weekday.${r.weekday}`)}</span>
                    <span className="text-sm font-normal text-muted-foreground">{t(r.open ? 'settings.hours.open' : 'settings.hours.closed')}</span>
                  </label>
                  {r.open && (
                    <span className="flex flex-wrap items-center gap-2" role="group" aria-label={t('settings.hours.timesFor', { day: t(`settings.weekday.${r.weekday}`) })}>
                      <Input type="time" dir="ltr" className="h-11 w-32" aria-label={t('settings.hours.opens')} aria-invalid={bad || undefined} value={r.opens} onChange={e => hours.onTime(r.weekday, 'opens', e.target.value)} />
                      <span aria-hidden>{'–'}</span>
                      <Input type="time" dir="ltr" className="h-11 w-32" aria-label={t('settings.hours.closes')} aria-invalid={bad || undefined} value={r.closes} onChange={e => hours.onTime(r.weekday, 'closes', e.target.value)} />
                      {bad && <span role="alert" className={fieldError}>{t('settings.hours.order')}</span>}
                    </span>
                  )}
                </li>
              );
            })}
          </ul>
        )}
        <div className="flex flex-wrap items-center gap-3" aria-live="polite">
          <Button type="button" className="min-h-11" loading={hours.saving} disabled={!hours.dirty || hours.problems.length > 0} onClick={hours.onSave}>{t('settings.save')}</Button>
          {hours.saved && <span>{t('settings.saved')}</span>}
          {hours.saveFailed && <span role="alert" className={fieldError}>{t('settings.saveFailed')}</span>}
        </div>
      </Section>
      <Section title={t('settings.schedules.title')} intro={t('settings.schedules.intro')}>
        {schedules.loading ? <p role="status">{t('settings.loading')}</p> : schedules.loadFailed ? <p role="alert" className={fieldError}>{t('settings.loadFailed')}</p>
          : schedules.calendars.length === 0 ? <p className="text-muted-foreground">{t('settings.schedules.none')}</p> : (
          <>
            <div className="flex flex-wrap gap-4">
              <Field id="schedule-calendar" label={t('settings.schedules.calendar')} className="min-w-60 flex-1">
                <Select value={schedules.calendarId} onValueChange={schedules.onCalendar}>
                  <SelectTrigger id="schedule-calendar" className="h-11 w-full"><SelectValue /></SelectTrigger>
                  <SelectContent>{schedules.calendars.map(c => <SelectItem key={c.calendarId} value={c.calendarId}>{c.name}</SelectItem>)}</SelectContent>
                </Select>
              </Field>
              <Field id="schedule-slot" label={t('settings.schedules.slot')} className="w-40">
                <Select value={String(schedules.slotMinutes)} onValueChange={v => schedules.onSlot(Number(v))}>
                  <SelectTrigger id="schedule-slot" className="h-11 w-full"><SelectValue /></SelectTrigger>
                  <SelectContent>{slotLengths.map(m => <SelectItem key={m} value={String(m)}>{t('settings.schedules.minutes', { minutes: m })}</SelectItem>)}</SelectContent>
                </Select>
              </Field>
            </div>
            {schedules.sessions.length === 0 && <p className="text-muted-foreground">{t('settings.schedules.noSessions')}</p>}
            <ul className="flex flex-col divide-y rounded-lg border">
              {schedules.sessions.map((s, i) => (
                // The row order is the user's, and rows carry no id: the index is the key.
                <li key={i} className="flex flex-wrap items-center gap-3 p-3">
                  <Select value={s.weekday} onValueChange={v => schedules.onSession(i, { weekday: v as typeof s.weekday })}>
                    <SelectTrigger className="h-11 w-44" aria-label={t('settings.schedules.day')}><SelectValue /></SelectTrigger>
                    <SelectContent>{weekdays.map(d => <SelectItem key={d} value={d}>{t(`settings.weekday.${d}`)}</SelectItem>)}</SelectContent>
                  </Select>
                  <Input type="time" dir="ltr" className="h-11 w-32" aria-label={t('settings.schedules.from')} value={s.start} onChange={e => schedules.onSession(i, { start: e.target.value })} />
                  <span aria-hidden>{'–'}</span>
                  <Input type="time" dir="ltr" className="h-11 w-32" aria-label={t('settings.schedules.to')} value={s.end} onChange={e => schedules.onSession(i, { end: e.target.value })} />
                  <Button type="button" variant="outline" className="min-h-11" onClick={() => schedules.onRemoveSession(i)}
                    aria-label={t('settings.schedules.removeSession', { day: t(`settings.weekday.${s.weekday}`), from: s.start, to: s.end })}>{t('settings.remove')}</Button>
                </li>
              ))}
            </ul>
            {schedules.problem && <p role="alert" className={fieldError}>{t(`settings.schedules.error.${schedules.problem}`)}</p>}
            <div className="flex flex-wrap items-center gap-3" aria-live="polite">
              <Button type="button" variant="outline" className="min-h-11" onClick={schedules.onAddSession}>{t('settings.schedules.addSession')}</Button>
              <Button type="button" className="min-h-11" loading={schedules.saving} disabled={!schedules.dirty || !!schedules.problem} onClick={schedules.onSave}>{t('settings.save')}</Button>
              {/* With visits left outside the hours, the warning below already opens with "Saved." */}
              {schedules.saved && schedules.outside.length === 0 && <span role="status" className="font-bold">{t('settings.saved')}</span>}
              {schedules.saveFailed && <span role="alert" className={fieldError}>{t('settings.saveFailed')}</span>}
            </div>
            {schedules.outside.length > 0 && (
              <div role="status" className="flex flex-col gap-1 rounded-lg border border-warning-700 p-3">
                <p className="font-bold">{t('settings.schedules.outside', { count: schedules.outside.length })}</p>
                <p className="text-sm">{schedules.outside.map(a => a.reference).join(', ')}</p>
              </div>
            )}
          </>
        )}
      </Section>
    </div>
  );
}
