import type { FormEventHandler } from 'react';
import { Controller, type UseFormReturn } from 'react-hook-form';
import { useI18n } from '@medvya/i18n';
import { Button, Switch } from '@medvya/ui';
import { templateNameKey, type NoteTemplate, type TemplateChoiceForm } from '../domain/note';

type Props = {
  form: UseFormReturn<TemplateChoiceForm>; templates: NoteTemplate[];
  loading: boolean; loadFailed: boolean; saving: boolean; saved: boolean; saveFailed: boolean;
  onSubmit: FormEventHandler<HTMLFormElement>;
};

/** The doctor picks which of the templates appear when switching a note's template (MDV-P2-025). */
export default function NoteTemplateChoice(p: Props) {
  const { t } = useI18n();
  if (p.loading) return <p role="status">{t('noteEditor.loading')}</p>;
  if (p.loadFailed) return <p role="alert" className="font-bold text-danger-700">{t('noteEditor.loadFailed')}</p>;
  return (
    <details className="rounded-xl border bg-card p-4">
      <summary className="min-h-11 cursor-pointer text-lg font-extrabold">{t('noteTemplates.choice.title')}</summary>
      <form noValidate onSubmit={p.onSubmit} className="mt-3 flex flex-col gap-3">
        <p className="text-sm text-muted-foreground">{t('noteTemplates.choice.intro')}</p>
        <Controller control={p.form.control} name="keys" render={({ field }) => (
          <ul aria-label={t('noteTemplates.choice.title')} className="flex flex-col divide-y rounded-lg border">
            {p.templates.map(tpl => (
              <li key={tpl.key} className="p-3">
                <label className="flex min-h-11 items-center gap-3 font-bold">
                  <Switch checked={field.value.includes(tpl.key)}
                    onCheckedChange={on => field.onChange(on ? [...field.value, tpl.key] : field.value.filter(k => k !== tpl.key))} />
                  <span>{t(templateNameKey(tpl.key))}</span>
                </label>
              </li>
            ))}
          </ul>
        )} />
        {p.form.formState.errors.keys && <p role="alert" className="font-bold text-danger-700">{t('noteTemplates.choice.minOne')}</p>}
        <div className="flex flex-wrap items-center gap-3" aria-live="polite">
          <Button type="submit" className="min-h-11" loading={p.saving}>{t('noteTemplates.choice.save')}</Button>
          {p.saved && <span>{t('noteTemplates.choice.saved')}</span>}
          {p.saveFailed && <span role="alert" className="font-bold text-danger-700">{t('noteTemplates.choice.saveFailed')}</span>}
        </div>
      </form>
    </details>
  );
}
