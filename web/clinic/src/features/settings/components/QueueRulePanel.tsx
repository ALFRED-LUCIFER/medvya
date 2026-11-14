import type { FormEventHandler } from 'react';
import type { UseFormReturn } from 'react-hook-form';
import { useI18n } from '@medvya/i18n';
import { Button, ChoiceRow } from '@medvya/ui';
import { queueOrders, type QueueRule } from '../domain/settings';
import { fieldError } from './fieldStyles';
import { Section } from './Section';

type Props = {
  form: UseFormReturn<QueueRule>; loading: boolean; forbidden: boolean; loadFailed: boolean;
  saving: boolean; saved: boolean; saveFailed: boolean; onSubmit: FormEventHandler<HTMLFormElement>;
};

/** The order arrived patients are seen in (MDV-P1-103): the doctor's day, the board and the queue display all follow it. */
export default function QueueRulePanel(p: Props) {
  const { t } = useI18n();
  if (p.loading) return <p role="status">{t('settings.loading')}</p>;
  if (p.loadFailed) return <p role="alert" className={fieldError}>{t(p.forbidden ? 'settings.forbidden' : 'settings.loadFailed')}</p>;
  return (
    <Section title={t('settings.queue.title')} intro={t('settings.queue.intro')}>
      <form noValidate onSubmit={p.onSubmit} className="flex flex-col gap-4">
        <fieldset className="flex flex-col gap-2">
          <legend className="sr-only">{t('settings.queue.title')}</legend>
          {queueOrders.map(order => (
            <ChoiceRow key={order} className="items-start gap-3 rounded-lg border p-3 [&>input]:mt-1" type="radio" value={order} {...p.form.register('order')} aria-describedby={`queue-${order}`}>
              <span className="flex flex-col">
                <strong>{t(`settings.queue.${order}`)}</strong>
                <span id={`queue-${order}`} className="text-sm text-muted-foreground">{t(`settings.queue.${order}.hint`)}</span>
              </span>
            </ChoiceRow>
          ))}
        </fieldset>
        <ChoiceRow className="gap-3 font-bold" type="checkbox" {...p.form.register('bookedFirst')}>
          {t('settings.queue.bookedFirst')}
        </ChoiceRow>
        <p className="text-sm text-muted-foreground">{t('settings.queue.escalation')}</p>
        <div className="flex flex-wrap items-center gap-3">
          <Button type="submit" loading={p.saving}>{t('settings.save')}</Button>
          {p.saved && <p role="status">{t('settings.saved')}</p>}
          {p.saveFailed && <p role="alert" className={fieldError}>{t('settings.saveFailed')}</p>}
        </div>
      </form>
    </Section>
  );
}
