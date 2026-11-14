import type { FormEventHandler } from 'react';
import { Controller, type UseFormReturn } from 'react-hook-form';
import { ltr, useI18n } from '@medvya/i18n';
import { Button, Field, Input, Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@medvya/ui';
import { verifications, type AddDraft, type DiagnosisCode } from '../domain/problems';

export type ProblemAddFormProps = {
  addForm: UseFormReturn<AddDraft>; search: string; results: DiagnosisCode[]; picked: DiagnosisCode | null; adding: boolean; online: boolean;
  onSearch: (q: string) => void; onPick: (c: DiagnosisCode) => void; onClearPick: () => void; onAdd: FormEventHandler<HTMLFormElement>;
  /** Inline in the consultation the search takes focus when the area opens; the problems page leaves focus where it is. */
  autoFocus?: boolean;
};

/** ICD-10 search by code or words and the add form (E-07), shared by the problems page and the consultation's Assessment so the two never drift. */
export default function ProblemAddForm(p: ProblemAddFormProps) {
  const { t } = useI18n();
  const { errors } = p.addForm.formState;
  return (
    <form noValidate onSubmit={p.onAdd} aria-labelledby="problems-add-title" className="flex flex-col gap-3 rounded-md border p-3">
      <h3 id="problems-add-title" className="font-extrabold">{t('problems.add.title')}</h3>
      {p.picked ? (
        <div className="flex flex-wrap items-center gap-2">
          <p role="status"><span className="font-bold">{ltr(p.picked.code)}</span>{` · ${p.picked.title}`}</p>
          <Button type="button" variant="outline" size="sm" onClick={p.onClearPick}>{t('problems.add.clearCode')}</Button>
        </div>
      ) : (
        <div className="flex flex-col gap-2">
          <Field id="problem-search" label={t('problems.add.search')} hint={t('problems.add.searchHint')}>
            <Input type="search" autoComplete="off" autoFocus={p.autoFocus} value={p.search} onChange={e => p.onSearch(e.target.value)} />
          </Field>
          {p.search.trim() !== '' && (p.results.length === 0
            ? <p role="status" className="text-sm">{t('problems.add.noResults')}</p>
            : <ul aria-label={t('problems.add.results')} className="flex flex-col gap-1">
                {p.results.map(c => (
                  <li key={c.code}><Button type="button" variant="ghost" className="w-full justify-start text-start" onClick={() => p.onPick(c)}><span className="font-bold">{ltr(c.code)}</span>{` · ${c.title}`}</Button></li>
                ))}
              </ul>)}
        </div>
      )}
      <div className="grid gap-3 sm:grid-cols-3">
        <Field id="problem-description" label={t('problems.add.description')} hint={t(p.picked ? 'problems.add.descriptionFromCode' : 'problems.add.descriptionHint')} error={errors.description ? t('problems.add.descriptionRequired') : null}>
          <Input autoComplete="off" maxLength={200} placeholder={p.picked?.title} {...p.addForm.register('description')} />
        </Field>
        <Controller control={p.addForm.control} name="verification" render={({ field }) => (
          <Select value={field.value} onValueChange={field.onChange}>
            <Field id="problem-verification" label={t('problems.col.verification')}>
              <SelectTrigger className="w-full"><SelectValue /></SelectTrigger>
            </Field>
            <SelectContent>{verifications.map(v => <SelectItem key={v} value={v}>{t(`problems.verification.${v}`)}</SelectItem>)}</SelectContent>
          </Select>
        )} />
        <Field id="problem-onset" label={t('problems.col.onset')}>
          <Input type="date" {...p.addForm.register('onsetOn')} />
        </Field>
      </div>
      <div><Button type="submit" loading={p.adding} disabled={!p.online}>{t('problems.add.submit')}</Button></div>
    </form>
  );
}
