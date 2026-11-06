import { describe, expect, it } from 'vitest';
import { notificationPrefsSchema, toPrefsForm, toPrefsPayload, type NotificationPrefs } from './notifications';

const saved: NotificationPrefs = { confirmation: true, reminder24h: false, reminder2h: true, changes: true, email: true, sms: false, quietStart: '21:00:00', quietEnd: '08:00:00' };

describe('notification preferences', () => {
  it('shows times as HH:mm and sends them back as HH:mm:ss', () => {
    const form = toPrefsForm(saved);
    expect(form.quietStart).toBe('21:00');
    expect(toPrefsPayload(form)).toEqual(saved);
  });

  it('rejects a time that is not HH:mm', () => {
    expect(notificationPrefsSchema.safeParse(toPrefsForm(saved)).success).toBe(true);
    expect(notificationPrefsSchema.safeParse({ ...toPrefsForm(saved), quietEnd: '' }).success).toBe(false);
    expect(notificationPrefsSchema.safeParse({ ...toPrefsForm(saved), quietEnd: '25:00' }).success).toBe(false);
  });
});
