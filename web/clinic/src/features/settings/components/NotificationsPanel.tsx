import type { FormEventHandler } from 'react';
import { Controller, type UseFormReturn } from 'react-hook-form';
import { useI18n } from '@medvya/i18n';
import { Button, Input, Switch } from '@medvya/ui';
import { channelToggles, messageToggles, previewGroups, type NotificationPrefsForm, type TemplatePreview } from '../domain/notifications';
import { describedBy, fieldError } from './fieldStyles';
import { Field } from './Field';
import { Section } from './Section';

type Props = {
  form: UseFormReturn<NotificationPrefsForm>;
  templates: TemplatePreview[] | null;
  loading: boolean;
  forbidden: boolean;
  loadFailed: boolean;
  saving: boolean;
  saved: boolean;
  saveFailed: boolean;
  onSubmit: FormEventHandler<HTMLFormElement>;
};

/** Notifications (F-08): which patient messages go out, on which channels, the quiet hours, and each template with placeholders only. */
export default function NotificationsPanel(p: Props) {
  const { t } = useI18n();
  const { errors } = p.form.formState;
  if (p.loading) return <p role="status">{t('settings.loading')}</p>;
  if (p.loadFailed) return <p role="alert" className={fieldError}>{t(p.forbidden ? 'settings.forbidden' : 'settings.loadFailed')}</p>;
  return (
    <div className="flex flex-col gap-4">
      <form noValidate onSubmit={p.onSubmit} className="flex flex-col gap-4">
        <Section title={t('settings.notifications.messages')} intro={t('settings.notifications.messagesIntro')}>
          <ul className="flex flex-col divide-y rounded-lg border">
            {messageToggles.map(name => (
              <li key={name} className="p-3">
                <Controller control={p.form.control} name={name} render={({ field }) => (
                  <label className="flex min-h-11 items-center gap-3 font-bold">
                    <Switch checked={field.value} onCheckedChange={field.onChange} />
                    <span>{t(`settings.notifications.message.${name}`)}</span>
                    <span className="text-sm font-normal text-muted-foreground">{t(field.value ? 'settings.notifications.on' : 'settings.notifications.off')}</span>
                  </label>
                )} />
              </li>
            ))}
          </ul>
          <p className="text-sm text-muted-foreground">{t('settings.notifications.turnOffNote')}</p>
        </Section>

        <Section title={t('settings.notifications.channels')} intro={t('settings.notifications.channelsIntro')}>
          <ul className="flex flex-col divide-y rounded-lg border">
            {channelToggles.map(name => (
              <li key={name} className="p-3">
                <Controller control={p.form.control} name={name} render={({ field }) => (
                  <label className="flex min-h-11 items-center gap-3 font-bold">
                    <Switch checked={field.value} onCheckedChange={field.onChange} />
                    <span>{t(`settings.notifications.channel.${name}`)}</span>
                    <span className="text-sm font-normal text-muted-foreground">{t(field.value ? 'settings.notifications.on' : 'settings.notifications.off')}</span>
                  </label>
                )} />
              </li>
            ))}
            <li className="p-3">
              <label className="flex min-h-11 items-center gap-3 font-bold">
                <Switch checked={false} disabled aria-describedby="notifications-whatsapp" />
                <span>{t('settings.notifications.channel.whatsapp')}</span>
              </label>
              <p id="notifications-whatsapp" className="text-sm text-muted-foreground">{t('settings.notifications.whatsappLater')}</p>
            </li>
          </ul>
          <p className="text-sm text-muted-foreground">{t('settings.notifications.senderNote')}</p>
        </Section>

        <Section title={t('settings.notifications.quiet')} intro={t('settings.notifications.quietIntro')}>
          <div className="flex flex-wrap gap-4">
            <Field id="quiet-start" label={t('settings.notifications.quietStart')} error={errors.quietStart && t('settings.error.required')}>
              <Input id="quiet-start" type="time" dir="ltr" className="h-11 w-32" {...describedBy('quiet-start', !!errors.quietStart)} {...p.form.register('quietStart')} />
            </Field>
            <Field id="quiet-end" label={t('settings.notifications.quietEnd')} error={errors.quietEnd && t('settings.error.required')}>
              <Input id="quiet-end" type="time" dir="ltr" className="h-11 w-32" {...describedBy('quiet-end', !!errors.quietEnd)} {...p.form.register('quietEnd')} />
            </Field>
          </div>
        </Section>

        <div className="flex flex-wrap items-center gap-3" aria-live="polite">
          <Button type="submit" className="min-h-11" loading={p.saving}>{t('settings.save')}</Button>
          {p.saved && <span>{t('settings.saved')}</span>}
          {p.saveFailed && <span role="alert" className={fieldError}>{t('settings.saveFailed')}</span>}
        </div>
      </form>

      <Section title={t('settings.notifications.templates')} intro={t('settings.notifications.templatesIntro')}>
        {p.templates === null ? <p role="status">{t('settings.loading')}</p> : (
          <ul className="flex flex-col gap-3">
            {previewGroups.map(group => p.templates?.find(x => x.group === group)).map(tpl => tpl && (
              <li key={tpl.group} className="flex flex-col gap-1 rounded-lg border p-3">
                <h3 className="font-bold">{t(`settings.notifications.group.${tpl.group as (typeof previewGroups)[number]}`)}</h3>
                <p className="text-sm font-semibold">{tpl.subject}</p>
                <p className="text-sm text-muted-foreground">{tpl.text}</p>
              </li>
            ))}
          </ul>
        )}
      </Section>
    </div>
  );
}
