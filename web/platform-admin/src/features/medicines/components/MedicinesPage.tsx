import { useId, type FormEventHandler } from 'react';
import { Ban, PencilLine, Plus, ShieldCheck } from 'lucide-react';
import { Controller, type UseFormReturn } from 'react-hook-form';
import { useI18n, type MessageKey } from '@medvya/i18n';
import {
  Button, Card, ErrorSummary, Field, Input, PageContainer, PageHeader, SegmentedControl, Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
  StickyActionBar, Table, TableBody, TableCell, TableHead, TableHeader, TableRow, TableState, Tag,
} from '@medvya/ui';
import ReasonPanel, { ReasonBar } from '../../shell/components/ReasonPanel';
import { classesFor, countries, errorKeyOf, forms, medicineColumns, type Country, type Editing, type Medicine, type MedicineForm, type PendingAction, type RegulatoryClass } from '../domain/medicines';

type Props = {
  vm: {
    canWrite: boolean; country: Country; query: string; onCountry: (c: Country) => void; onQuery: (q: string) => void;
    state: 'loading' | 'error' | 'empty' | 'ready'; medicines: Medicine[];
    editing: Editing | null; form: UseFormReturn<MedicineForm>; saving: boolean; saveProblem: string | null;
    onStartAdd: () => void; onStartCorrect: (m: Medicine) => void; onCancelEdit: () => void; onSave: FormEventHandler;
    pending: PendingAction | null; reason: UseFormReturn<{ reason: string }>; classChoice: RegulatoryClass; onClassChoice: (c: RegulatoryClass) => void;
    acting: boolean; actProblem: string | null;
    onStartAction: (a: PendingAction) => void; onCancelAction: () => void; onConfirmAction: FormEventHandler;
  };
};

const formId = 'medicine-form';

/**
 * The Medvya-wide medicine list (MDV-P5-064): each country's shared formulary, which every clinic of that country reads. Every role searches it;
 * owner and operations add, correct, set a class after an advisor's review and deactivate, each with a reason (platform audit log).
 */
export default function MedicinesPage({ vm }: Props) {
  const { t } = useI18n();
  const words = (code?: string | null) => (code ? t(errorKeyOf(code) as MessageKey) : undefined);
  const nameOf = (m: Medicine) => `${m.generic} ${m.strength}`;
  return (
    <PageContainer>
      <PageHeader title={t('platform.medicines.title')} description={t('platform.medicines.lead')} />
      <div className="flex flex-wrap items-end gap-4">
        <SegmentedControl label={t('platform.medicines.country')} value={vm.country} onValueChange={vm.onCountry}
          options={countries.map(c => ({ value: c, label: t(`platform.medicines.country.${c}`) }))} />
        <Field id="medicine-search" label={t('platform.medicines.search')} hint={t('platform.medicines.searchHint')} className="min-w-0 grow md:max-w-md">
          <Input type="search" autoComplete="off" value={vm.query} onChange={e => vm.onQuery(e.target.value)} />
        </Field>
      </div>

      {vm.editing && <MedicineEditor vm={vm} editing={vm.editing} words={words} />}

      {vm.pending && (
        <ReasonPanel form={vm.reason} onConfirm={vm.onConfirmAction}
          title={t(vm.pending.kind === 'class' ? 'platform.medicines.setClassFor' : 'platform.medicines.deactivateFor', { name: nameOf(vm.pending.medicine) })}>
          <p className="text-sm">{t(vm.pending.kind === 'class' ? 'platform.medicines.setClassBody' : 'platform.medicines.deactivateBody')}</p>
          {vm.pending.kind === 'class' && (
            <Select value={vm.classChoice} onValueChange={v => vm.onClassChoice(v as RegulatoryClass)}>
              <Field id="medicine-class-choice" label={t('formulary.field.regClass')} required>
                <SelectTrigger><SelectValue /></SelectTrigger>
              </Field>
              <SelectContent>{classesFor(vm.pending.medicine.country).map(c => <SelectItem key={c} value={c}>{t(`formulary.class.${c}`)}</SelectItem>)}</SelectContent>
            </Select>
          )}
        </ReasonPanel>
      )}

      <TableState state={vm.state} messages={{ loading: t('platform.state.loading'), error: t('platform.state.error'), empty: t('platform.medicines.empty') }}>
        {/* Stacked under 768 px with the medicine first, so nothing scrolls sideways on a phone (MDV-P2-054). */}
        <Table label={t('platform.medicines.table')} stacked>
          <TableHeader>
            <TableRow>
              {medicineColumns.map(c => <TableHead key={c} scope="col">{t(c)}</TableHead>)}
              {vm.canWrite && <TableHead scope="col"><span className="sr-only">{t('platform.medicines.actions')}</span></TableHead>}
            </TableRow>
          </TableHeader>
          <TableBody>
            {vm.medicines.map(m => (
              <TableRow key={m.id}>
                <TableHead scope="row" className="font-normal whitespace-normal">
                  <p className="font-semibold">{`${m.generic} ${m.strength}`}{' · '}{t(`formulary.form.${m.form}` as MessageKey)}</p>
                  {m.brands.length > 0 && <p className="text-sm text-muted-foreground">{t('platform.medicines.brands', { list: m.brands.join(', ') })}</p>}
                </TableHead>
                <TableCell label={t('platform.medicines.col.class')}>
                  {/* Not verified is said in words, never by colour alone. */}
                  {m.regClass === 'NotVerified' ? <Tag tone="warning" label={t('formulary.class.NotVerified')} size="sm" /> : t(`formulary.class.${m.regClass}`)}
                </TableCell>
                <TableCell label={t('platform.medicines.col.source')}>
                  {t(`formulary.source.${m.source}`)}
                  {m.registerCode && <p dir="ltr" className="text-sm text-muted-foreground">{t('platform.medicines.register', { code: m.registerCode, release: m.registerRelease ?? '' })}</p>}
                </TableCell>
                <TableCell label={t('platform.medicines.col.status')}>{t(m.active ? 'platform.medicines.active' : 'platform.medicines.inactive')}</TableCell>
                {vm.canWrite && (
                  <TableCell>
                    <div className="flex flex-wrap gap-2">
                      <Button variant="outline" aria-label={t('platform.medicines.correctFor', { name: nameOf(m) })} onClick={() => vm.onStartCorrect(m)}>
                        <PencilLine aria-hidden />{t('platform.medicines.correct')}</Button>
                      <Button variant="outline" aria-label={t('platform.medicines.setClassFor', { name: nameOf(m) })} onClick={() => vm.onStartAction({ kind: 'class', medicine: m })}>
                        <ShieldCheck aria-hidden />{t('platform.medicines.setClass')}</Button>
                      {m.active && (
                        <Button variant="ghost" aria-label={t('platform.medicines.deactivateFor', { name: nameOf(m) })} onClick={() => vm.onStartAction({ kind: 'deactivate', medicine: m })}>
                          <Ban aria-hidden />{t('platform.medicines.deactivate')}</Button>
                      )}
                    </div>
                  </TableCell>
                )}
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableState>

      {/* The screen's own actions, in the bottom bar (DESIGN.md Screen layout standard, MDV-P2-065). */}
      {vm.pending ? (
        <ReasonBar confirmLabel={t(vm.pending.kind === 'class' ? 'platform.medicines.setClass' : 'platform.medicines.deactivate')}
          destructive={vm.pending.kind === 'deactivate'} busy={vm.acting} problem={words(vm.actProblem) ?? null} onCancel={vm.onCancelAction} />
      ) : vm.editing ? (
        // Keyed: a reused button would turn into the submit mid-click (see the team page).
        <StickyActionBar key="edit" status={vm.saveProblem && <p role="alert" className="text-sm font-semibold text-danger-700">{words(vm.saveProblem)}</p>}>
          <Button type="button" variant="outline" onClick={vm.onCancelEdit}>{t('platform.action.cancel')}</Button>
          <Button type="submit" form={formId} loading={vm.saving}>{t(vm.editing.kind === 'add' ? 'platform.medicines.add' : 'platform.medicines.saveCorrection')}</Button>
        </StickyActionBar>
      ) : vm.canWrite ? (
        <StickyActionBar key="actions">
          <Button onClick={vm.onStartAdd}><Plus aria-hidden />{t('platform.medicines.add')}</Button>
        </StickyActionBar>
      ) : null}
    </PageContainer>
  );
}

function MedicineEditor({ vm, editing, words }: { vm: Props['vm']; editing: Editing; words: (code?: string | null) => string | undefined }) {
  const { t } = useI18n();
  const base = useId();
  const id = (f: keyof MedicineForm) => `${base}-${f}`;
  const e = vm.form.formState.errors;
  const labels: Record<keyof MedicineForm, MessageKey> = {
    generic: 'formulary.field.generic', strength: 'formulary.field.strength', form: 'formulary.field.form', brands: 'formulary.field.brands',
    regClass: 'formulary.field.regClass', nationalCode: 'formulary.field.nationalCode', atc: 'formulary.field.atc', defaultSig: 'formulary.field.defaultSig',
    reason: 'platform.reason.label',
  };
  const summary = (Object.keys(labels) as (keyof MedicineForm)[]).flatMap(f => (e[f]?.message ? [{ id: id(f), label: t(labels[f]), message: words(e[f]?.message)! }] : []));
  const text = (f: 'generic' | 'strength' | 'brands' | 'nationalCode' | 'atc' | 'defaultSig', extra: { required?: boolean; hint?: string; ltr?: boolean } = {}) => (
    <Field id={id(f)} label={t(labels[f])} required={extra.required} hint={extra.hint} error={words(e[f]?.message)}>
      <Input autoComplete="off" dir={extra.ltr ? 'ltr' : undefined} {...vm.form.register(f)} />
    </Field>
  );
  return (
    <Card title={editing.kind === 'add'
      ? t('platform.medicines.addTitle', { country: t(`platform.medicines.country.${vm.country}`) })
      : t('platform.medicines.correctFor', { name: `${editing.medicine.generic} ${editing.medicine.strength}` })}>
      <p className="text-sm">{t(editing.kind === 'add' ? 'platform.medicines.addBody' : 'platform.medicines.correctBody')}</p>
      <form id={formId} onSubmit={vm.onSave} noValidate className="grid gap-3 md:grid-cols-2">
        <ErrorSummary title={t('field.errorSummary')} focusKey={vm.form.formState.submitCount} errors={summary} className="md:col-span-2" />
        {/* The optional fields say so in their labels (the clinic's formulary words). */}
        {text('generic', { required: true })}
          {text('strength', { required: true })}
          <Controller control={vm.form.control} name="form" render={({ field }) => (
            <Select value={field.value} onValueChange={field.onChange}>
              <Field id={id('form')} label={t(labels.form)} required error={words(e.form?.message)}>
                <SelectTrigger><SelectValue /></SelectTrigger>
              </Field>
              <SelectContent>{forms.map(f => <SelectItem key={f} value={f}>{t(`formulary.form.${f}`)}</SelectItem>)}</SelectContent>
            </Select>
          )} />
          {editing.kind === 'add' && (
            <Controller control={vm.form.control} name="regClass" render={({ field }) => (
              <Select value={field.value} onValueChange={field.onChange}>
                <Field id={id('regClass')} label={t(labels.regClass)} required error={words(e.regClass?.message)}>
                  <SelectTrigger><SelectValue /></SelectTrigger>
                </Field>
                <SelectContent>{classesFor(vm.country).map(c => <SelectItem key={c} value={c}>{t(`formulary.class.${c}`)}</SelectItem>)}</SelectContent>
              </Select>
            )} />
          )}
          {text('brands', { hint: t('formulary.field.brands.hint') })}
          {text('atc', { ltr: true })}
          {text('nationalCode', { ltr: true })}
          {text('defaultSig')}
          <Field id={id('reason')} label={t('platform.reason.label')} required hint={t('platform.reason.hint')} error={words(e.reason?.message)} className="md:col-span-2">
            <Input autoComplete="off" {...vm.form.register('reason')} />
          </Field>
      </form>
    </Card>
  );
}
