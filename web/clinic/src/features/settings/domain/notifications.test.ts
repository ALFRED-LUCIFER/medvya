import { describe, expect, it } from 'vitest';
import { notificationPrefsSchema, toPrefsForm, toPrefsPayload, type NotificationPrefs } from './notifications';

const saved: NotificationPrefs = { confirmation: true, reminder24h: false, reminder2h: true, changes: true, email: true, sms: false, quietStart: '21:00:00', quietEnd: '08:00:00', senderName: null };

describe('notification preferences', () => {
  it('shows times as HH:mm and sends them back as HH:mm:ss', () => {
    const form = toPrefsForm(saved);
    expect(form.quietStart).toBe('21:00');
    expect(toPrefsPayload(form)).toEqual(saved);
  });

  it('sends an empty sender name as null (the location name is used) and trims a set one (#213)', () => {
    expect(toPrefsForm(saved).senderName).toBe('');
    expect(toPrefsPayload({ ...toPrefsForm(saved), senderName: '  Iyer Family Clinic ' }).senderName).toBe('Iyer Family Clinic');
    expect(notificationPrefsSchema.safeParse({ ...toPrefsForm(saved), senderName: 'x'.repeat(61) }).success).toBe(false);
  });

  it('rejects a time that is not HH:mm', () => {
    expect(notificationPrefsSchema.safeParse(toPrefsForm(saved)).success).toBe(true);
    expect(notificationPrefsSchema.safeParse({ ...toPrefsForm(saved), quietEnd: '' }).success).toBe(false);
    expect(notificationPrefsSchema.safeParse({ ...toPrefsForm(saved), quietEnd: '25:00' }).success).toBe(false);
  });
});
