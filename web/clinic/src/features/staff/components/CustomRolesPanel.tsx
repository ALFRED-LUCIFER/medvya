import { Lock } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, Input, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Switch } from '@medvya/ui';
import type { FormEventHandler } from 'react';
import type { UseFormReturn } from 'react-hook-form';
import { describedBy, fieldError } from '../../settings/components/fieldStyles';
import { Field, Section } from '../../settings/components/parts';
import type { Capability } from '../../session/domain/session';
import { addableRows, assignableRoles, type CustomRole, type RoleCatalogue, type RoleForm } from '../domain/staff';

type Props = {
  catalogue: RoleCatalogue;
  editing: CustomRole | 'new' | null;
  form: UseFormReturn<RoleForm>;
  extras: Capability[];
  saving: boolean;
  saveError: 'duplicate' | 'failed' | null;
  onOpen: (role: CustomRole | 'new') => void;
  onClose: () => void;
  onBase: (baseRole: RoleForm['baseRole']) => void;
  onToggle: (capability: Capability, on: boolean) => void;
  onSubmit: FormEventHandler<HTMLFormElement>;
};

/**
 * Custom roles (F-10): a built-in role whose rows stay locked, plus extra rows from the practice admin's own access.
 * Rows the server does not offer (clinical access, staff administration) cannot be added.
 */
export default function CustomRolesPanel(p: Props) {
  const { t } = useI18n();
  const { errors } = p.form.formState;
  const base = p.form.watch('baseRole');
  const addable = addableRows(p.catalogue, base);
  const lockedCount = (p.catalogue.builtIn.find(b => b.role === base)?.rows ?? []).filter(r => r.access !== 'None').length;
  return (
    <Section title={t('staff.roles.title')} intro={t('staff.roles.intro')}>
      {p.catalogue.custom.length === 0 && <p className="text-muted-foreground">{t('staff.roles.empty')}</p>}
      <ul className="flex flex-col divide-y rounded-lg border empty:hidden">
        {p.catalogue.custom.map(r => (
          <li key={r.id} className="flex flex-wrap items-center justify-between gap-3 p-3">
            <span className="flex min-w-0 flex-col">
              <span className="font-bold">{r.name}</span>
              <span className="text-sm text-muted-foreground">{[t(`role.${r.baseRole}`), ...r.capabilities.map(c => t(`capability.${c}`))].join(' · ')}</span>
            </span>
            <Button type="button" variant="outline" className="min-h-11 min-w-11" aria-label={t('staff.roles.editNamed', { name: r.name })} onClick={() => p.onOpen(r)}>{t('settings.edit')}</Button>
          </li>
        ))}
      </ul>
      {!p.editing && <Button type="button" variant="outline" className="self-start" onClick={() => p.onOpen('new')}>{t('staff.roles.add')}</Button>}
      {p.editing && (
        <form noValidate onSubmit={p.onSubmit} className="flex flex-col gap-4">
          <div className="grid gap-4 sm:grid-cols-2">
            <Field id="role-name" label={t('staff.roles.name')} error={errors.name && t('settings.error.required')}>
              <Input id="role-name" className="h-11" autoComplete="off" maxLength={60} {...describedBy('role-name', !!errors.name)} {...p.form.register('name')} />
            </Field>
            <Field id="role-base" label={t('staff.roles.base')} hint={t('staff.roles.baseHint', { count: lockedCount })}>
              <Select value={base} onValueChange={v => p.onBase(v as RoleForm['baseRole'])}>
                <SelectTrigger id="role-base" className="h-11 w-full"><SelectValue /></SelectTrigger>
                <SelectContent>{assignableRoles.map(r => <SelectItem key={r} value={r}>{t(`role.${r}`)}</SelectItem>)}</SelectContent>
              </Select>
            </Field>
          </div>
          <fieldset className="flex flex-col gap-2">
            <legend className="mb-1 font-bold">{t('staff.roles.extras')}</legend>
            <p className="text-sm text-muted-foreground">{t('staff.roles.extrasHint')}</p>
            {addable.length === 0 && <p>{t('staff.roles.extrasNone')}</p>}
            {addable.map(r => (
              <div key={r.capability} className="flex min-h-11 items-center justify-between gap-3 rounded-lg border p-3">
                <label htmlFor={`extra-${r.capability}`} className="flex flex-col font-semibold">
                  {t(`capability.${r.capability}`)}
                  <span className="text-sm font-normal text-muted-foreground">{t(`access.${r.extra!}`)}</span>
                </label>
                <Switch id={`extra-${r.capability}`} checked={p.extras.includes(r.capability)} onCheckedChange={on => p.onToggle(r.capability, on)} />
              </div>
            ))}
            <p className="flex items-center gap-2 text-sm text-muted-foreground"><Lock aria-hidden className="size-4" />{t('staff.roles.locked')}</p>
          </fieldset>
          {p.saveError && <p role="alert" className={fieldError}>{t(`staff.roles.error.${p.saveError}`)}</p>}
          <div className="flex flex-wrap gap-2">
            <Button type="submit" loading={p.saving}>{t('settings.save')}</Button>
            <Button type="button" variant="outline" onClick={p.onClose}>{t('settings.cancel')}</Button>
          </div>
        </form>
      )}
    </Section>
  );
}
