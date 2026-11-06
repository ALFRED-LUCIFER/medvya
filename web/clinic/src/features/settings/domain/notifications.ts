import { z } from 'zod';

// Mirrors src/Modules/Notifications/Medvya.Notifications.Application/INotifications.cs (NotificationPreferencesView, TemplatePreview).
// The server is the authority; these checks only save a round trip. Times are "HH:mm:ss" facility wall-clock times.
export type NotificationPrefs = {
  confirmation: boolean; reminder24h: boolean; reminder2h: boolean; changes: boolean;
  email: boolean; sms: boolean;
  quietStart: string; quietEnd: string;
};
export type TemplatePreview = { group: string; subject: string; text: string };

/** The message groups the practice can switch off, in the order the screen lists them. */
export const messageToggles = ['confirmation', 'reminder24h', 'reminder2h', 'changes'] as const satisfies readonly (keyof NotificationPrefs)[];
export const channelToggles = ['email', 'sms'] as const satisfies readonly (keyof NotificationPrefs)[];

const time = z.string().regex(/^([01]\d|2[0-3]):[0-5]\d$/);
export const notificationPrefsSchema = z.object({
  confirmation: z.boolean(), reminder24h: z.boolean(), reminder2h: z.boolean(), changes: z.boolean(),
  email: z.boolean(), sms: z.boolean(),
  quietStart: time, quietEnd: time,
});
export type NotificationPrefsForm = z.infer<typeof notificationPrefsSchema>;

const hhmm = (t: string) => t.slice(0, 5);
export const toPrefsForm = (p: NotificationPrefs): NotificationPrefsForm => ({ ...p, quietStart: hhmm(p.quietStart), quietEnd: hhmm(p.quietEnd) });
export const toPrefsPayload = (f: NotificationPrefsForm): NotificationPrefs => ({ ...f, quietStart: `${f.quietStart}:00`, quietEnd: `${f.quietEnd}:00` });

/** The preview groups in the catalogue map to these screen labels (message keys settings.notifications.group.*). */
export const previewGroups = ['Confirmation', 'Reminder', 'Rescheduled', 'Cancelled', 'NeedsRebooking'] as const;
