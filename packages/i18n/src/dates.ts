const day = (zone: string) => new Intl.DateTimeFormat('en-CA', { timeZone: zone, year: 'numeric', month: '2-digit', day: '2-digit' });

/** The calendar date (YYYY-MM-DD) of an instant in an IANA zone: the facility's day, whatever the device's zone is. */
export const dateIn = (at: Date, zone: string) => day(zone).format(at);

/** Today (YYYY-MM-DD) in an IANA zone, e.g. a date input's default. */
export const todayIn = (zone: string, now = new Date()) => dateIn(now, zone);

/** Whether the device is in this zone; both are resolved first, since the engine may name it by an alias (Asia/Calcutta and Asia/Kolkata are one zone). */
export const isDeviceTimeZone = (zone: string) =>
  new Intl.DateTimeFormat('en', { timeZone: zone }).resolvedOptions().timeZone === Intl.DateTimeFormat().resolvedOptions().timeZone;
