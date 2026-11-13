import { ltr, useI18n } from '@medvya/i18n';
import { Button, Card, Label, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Textarea } from '@medvya/ui';
import type { FormEventHandler } from 'react';
import type { UseFormReturn } from 'react-hook-form';
import type { HistoryEntry, HistorySection } from '../domain/chart';
import { bloodGroups, maxHistoryText, type BloodGroupForm, type HistoryForm, type SectionView } from '../domain/history';

type Props = {
  sections: SectionView[]; bloodGroup: string | null; timeZone: string; canEdit: boolean; editing: HistorySection | null;
  form: UseFormReturn<HistoryForm>; bloodForm: UseFormReturn<BloodGroupForm>; saving: boolean; problem: 'refused' | 'failed' | null;
  onEdit: (section: HistorySection) => void; onCancel: () => void; onSave: FormEventHandler; onSaveBloodGroup: FormEventHandler;
};

/**
 * History sections and blood group (MDV-P2-032, E-01). Each section shows its current text with who wrote it and when; a save adds a version,
 * and the earlier ones stay readable below it. The sections offered follow the life stage; one written at another stage is still shown.
 */
export default function HistorySections(p: Props) {
  const { t } = useI18n();
  return (
    <Card title={t('history.title')}>
      {p.problem && <p role="alert" className="font-bold">{t(`history.problem.${p.problem}`)}</p>}
      {/* A compact two-column list (UX-09): each section is a short tile with its own Edit; the one being edited takes the full width. */}
      <ul className="grid gap-3 md:grid-cols-2">
        <li className={tile(p.editing === 'BloodGroup')}>
          <Head name={t('history.section.BloodGroup')} edit={p.canEdit && p.editing !== 'BloodGroup' ? () => p.onEdit('BloodGroup') : undefined} />
          {p.editing === 'BloodGroup' ? (
            <form onSubmit={p.onSaveBloodGroup} className="flex flex-wrap items-end gap-2">
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="history-blood-group">{t('history.bloodGroup.choose')}</Label>
                <Select value={p.bloodForm.watch('text') || undefined} onValueChange={v => p.bloodForm.setValue('text', v as BloodGroupForm['text'], { shouldValidate: true })}>
                  <SelectTrigger id="history-blood-group" className="w-32"><SelectValue placeholder={t('history.bloodGroup.placeholder')} /></SelectTrigger>
                  <SelectContent>{bloodGroups.map(g => <SelectItem key={g} value={g}>{ltr(g)}</SelectItem>)}</SelectContent>
                </Select>
              </div>
              <Actions saving={p.saving} onCancel={p.onCancel} />
              {p.bloodForm.formState.errors.text && <p className="basis-full text-sm font-bold">{t('history.bloodGroup.required')}</p>}
            </form>
          ) : <span className={p.bloodGroup ? undefined : 'text-muted-foreground'}>{p.bloodGroup ? ltr(p.bloodGroup) : t('history.notRecorded')}</span>}
        </li>
        {p.sections.map(s => <Section key={s.section} view={s} p={p} />)}
      </ul>
    </Card>
  );
}

const tile = (editing: boolean) => `flex min-w-0 flex-col gap-2 rounded-lg border p-3 ${editing ? 'md:col-span-2' : ''}`;

/** A tile's heading row: the section name and, when this role may change it, its Edit button. */
function Head({ name, edit }: { name: string; edit?: () => void }) {
  const { t } = useI18n();
  return (
    <div className="flex flex-wrap items-center justify-between gap-2">
      <h3 className="font-bold">{name}</h3>
      {edit && <Button type="button" variant="outline" onClick={edit}>{t('history.edit', { section: name })}</Button>}
    </div>
  );
}

function Section({ view, p }: { view: SectionView; p: Props }) {
  const { t } = useI18n();
  const name = t(`history.section.${view.section}`);
  const id = `history-${view.section}`;
  const editing = p.editing === view.section;
  return (
    <li className={tile(editing)}>
      <Head name={name} edit={p.canEdit && !editing ? () => p.onEdit(view.section) : undefined} />
      {editing ? (
        <form onSubmit={p.onSave} className="flex flex-col gap-2">
          <Label htmlFor={id} className="sr-only">{name}</Label>
          <Textarea id={id} rows={4} maxLength={maxHistoryText} aria-invalid={!!p.form.formState.errors.text} {...p.form.register('text')} />
          {p.form.formState.errors.text && <p className="text-sm font-bold">{t('history.required')}</p>}
          <div className="flex flex-wrap gap-2"><Actions saving={p.saving} onCancel={p.onCancel} /></div>
        </form>
      ) : view.current ? <Version entry={view.current} zone={p.timeZone} /> : <p className="text-muted-foreground">{t('history.notRecorded')}</p>}
      {view.earlier.length > 0 && (
        <details className="text-sm">
          <summary className="min-h-11 cursor-pointer content-center">{t('history.earlier', { count: view.earlier.length })}</summary>
          <ul className="flex flex-col gap-2 ps-4">{view.earlier.map((e, i) => <li key={i}><Version entry={e} zone={p.timeZone} /></li>)}</ul>
        </details>
      )}
    </li>
  );
}

function Version({ entry, zone }: { entry: HistoryEntry; zone: string }) {
  const { t, formatDate } = useI18n();
  const date = formatDate(new Date(entry.recordedAt), zone);
  return (
    <div className="flex flex-col">
      <p dir="auto" className="whitespace-pre-wrap">{entry.text}</p>
      <span className="text-sm text-muted-foreground">{entry.author ? t('history.by', { author: entry.author, date }) : t('history.on', { date })}</span>
    </div>
  );
}

function Actions({ saving, onCancel }: { saving: boolean; onCancel: () => void }) {
  const { t } = useI18n();
  return (
    <>
      <Button type="submit" disabled={saving}>{t('history.save')}</Button>
      <Button type="button" variant="outline" onClick={onCancel}>{t('history.cancel')}</Button>
    </>
  );
}
