import type { FormEventHandler } from 'react';
import type { UseFormReturn } from 'react-hook-form';
import { CircleCheck } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, Textarea } from '@medvya/ui';
import { describedBy, fieldError } from '../../settings/components/fieldStyles';
import { Field } from '../../settings/components/Field';
import { Section } from '../../settings/components/Section';
import { allergyClasses, type BrandProblem, type BrandRowsForm } from '../domain/formulary';

type Props = {
  form: UseFormReturn<BrandRowsForm>;
  saving: boolean;
  added: number | null;
  problems: BrandProblem[];
  onSubmit: FormEventHandler<HTMLFormElement>;
};

/** Brand names in bulk (F-13, MDV-P1-088): one pasted row a brand, each naming a medicine already in the list. All rows are added, or none. */
export default function BrandsPanel(p: Props) {
  const { t } = useI18n();
  const refused = p.problems.length > 0;
  return (
    <Section title={t('formulary.brands.title')} intro={t('formulary.brands.intro')}>
      <form noValidate onSubmit={p.onSubmit} className="flex flex-col gap-4">
        <Field id="brand-rows" label={t('formulary.brands.rows')} hint={t('formulary.brands.rows.hint', { classes: allergyClasses.join(', ') })}>
          <Textarea id="brand-rows" rows={8} dir="auto" autoComplete="off" spellCheck={false}
            {...describedBy('brand-rows', refused, true)} {...p.form.register('rows')} />
        </Field>
        {refused && (
          <div id="brand-rows-error" role="alert" className={`flex flex-col gap-1 ${fieldError}`}>
            <p>{t('formulary.brands.refused')}</p>
            <ul className="list-disc ps-5">
              {p.problems.map(x => <li key={`${x.row}-${x.code}`}>{t(`formulary.brands.error.${x.code}`, { row: x.row ?? 0 })}</li>)}
            </ul>
          </div>
        )}
        {p.added !== null && <p role="status" className="flex items-center gap-2 font-bold"><CircleCheck aria-hidden className="size-5" />{t('formulary.brands.saved', { count: p.added })}</p>}
        <Button type="submit" className="self-start" loading={p.saving}>{t('formulary.brands.submit')}</Button>
      </form>
    </Section>
  );
}
