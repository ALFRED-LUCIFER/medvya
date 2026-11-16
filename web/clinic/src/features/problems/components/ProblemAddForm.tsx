import type { FormEventHandler } from 'react';
import { type UseFormReturn } from 'react-hook-form';
import { ltr, useI18n } from '@medvya/i18n';
import { Button, Field, Input } from '@medvya/ui';
import { verifications, type AddDraft, type DiagnosisCode } from '../domain/problems';
import { SelectField } from '../../forms/components/SelectField';

export type ProblemAddFormProps = {
  addForm: UseFormReturn<AddDraft>; search: string; results: DiagnosisCode[]; picked: DiagnosisCode | null; adding: boolean; online: boolean;
  onSearch: (q: string) => void; onPick: (c: DiagnosisCode) => void; onClearPick: () => void; onAdd: FormEventHandler<HTMLFormElement>;
  /** Inline in the consultation the search takes focus when the area opens; the problems page leaves focus where it is. */
  autoFocus?: boolean;
  /** The problems page names the form and puts its submit in the bottom bar (MDV-P2-064); inline in the consultation it stays in the form. */
  formId?: string;
};

/** ICD-10 search by code or words and the add form (E-07), shared by the problems page and the consultation's Assessment so the two never drift. */
export default function ProblemAddForm(p: ProblemAddFormProps) {
  const { t } = useI18n();
  const { errors } = p.addForm.formState;
  return (
    <form id={p.formId} noValidate onSubmit={p.onAdd} aria-labelledby="problems-add-title" className="flex flex-col gap-3 rounded-md border p-3">
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
      <div className="grid gap-3 md:grid-cols-3">
        <Field id="problem-description" label={t('problems.add.description')} hint={t(p.picked ? 'problems.add.descriptionFromCode' : 'problems.add.descriptionHint')} error={errors.description ? t('problems.add.descriptionRequired') : null}>
          <Input autoComplete="off" maxLength={200} placeholder={p.picked?.title} {...p.addForm.register('description')} />
        </Field>
        <SelectField control={p.addForm.control} name="verification" id="problem-verification" label={t('problems.col.verification')}
          options={verifications} optionLabel={v => t(`problems.verification.${v}`)} />
        <Field id="problem-onset" label={t('problems.col.onset')}>
          <Input type="date" {...p.addForm.register('onsetOn')} />
        </Field>
      </div>
      {!p.formId && <div><ProblemAddSubmit {...p} /></div>}
    </form>
  );
}

export function ProblemAddSubmit({ formId, adding, online }: Pick<ProblemAddFormProps, 'formId' | 'adding' | 'online'>) {
  const { t } = useI18n();
  return <Button type="submit" form={formId} loading={adding} disabled={!online}>{t('problems.add.submit')}</Button>;
}
