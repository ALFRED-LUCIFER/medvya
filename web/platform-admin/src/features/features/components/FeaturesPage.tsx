import { useId, type FormEventHandler } from 'react';
import { Lock, Power, SlidersHorizontal, TriangleAlert } from 'lucide-react';
import { Controller, type UseFormReturn } from 'react-hook-form';
import { useI18n, type MessageKey } from '@medvya/i18n';
import {
  Button, Card, ErrorSummary, Field, Input, PageContainer, PageHeader, SegmentedControl, Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
  StickyActionBar, Table, TableBody, TableCell, TableHead, TableHeader, TableRow, TableState, Tag, Textarea,
} from '@medvya/ui';
import ReasonPanel, { ReasonBar } from '../../shell/components/ReasonPanel';
import type { ReasonForm } from '../../team/domain/team';
import { featureColumns, hasName, needsSignOff, signOffState, stages, type PendingChange, type Release, type ReleaseForm, type Stage } from '../domain/features';

type Props = {
  vm: {
    state: 'loading' | 'error' | 'ready'; forbidden: boolean; countries: string[]; country: string | null; onCountry: (c: string) => void;
    rows: Release[]; canChange: boolean; pending: PendingChange | null; changeForm: UseFormReturn<ReleaseForm>; offForm: UseFormReturn<ReasonForm>;
    busy: boolean; problem: 'locked' | 'failed' | null; stage: Stage | undefined;
    onStartChange: (r: Release) => void; onStartOff: (r: Release) => void; onCancel: () => void; onSave: FormEventHandler; onTurnOff: FormEventHandler;
  };
};

const changeFormId = 'feature-change';

/**
 * Features (MDV-P5-041, design: Medvya Admin, Features and rollouts): Medvya's switch for each feature per country, on top of each clinic's own. Always-on
 * features are locked. Every change carries a reason for the platform audit log; a clinical feature reaches everyone only with a recorded clinical sign-off.
 */
export default function FeaturesPage({ vm }: Props) {
  const { t, formatDate, formatTime } = useI18n();
  const ids = { stage: useId(), percentage: useId(), organisations: useId(), signOff: useId(), reason: useId() };
  const name = (key: string) => (hasName(key) ? t(`platform.features.name.${key}`) : key);
  const stageText = (r: Release) => (r.stage === 'Percentage' ? t('platform.features.stage.percentageOf', { percent: r.percentage }) : t(`platform.features.stage.${r.stage}`));
  // In words and an icon, never colour alone: a clinical feature that reaches everyone without a recorded sign-off (F-043).
  const signOffText = (r: Release) => {
    const state = signOffState(r);
    return state === 'recorded' ? r.clinicalSignOff
      : state === 'missing' ? <span className="inline-flex items-center gap-1 font-semibold text-danger-text"><TriangleAlert aria-hidden className="size-4 shrink-0" />{t('platform.features.signOffMissing')}</span>
      : t(state === 'none' ? 'platform.features.noSignOff' : 'platform.features.notNeeded');
  };
  const errorText = (code?: string) => (code ? t(`platform.error.${code}` as MessageKey) : undefined);
  const e = vm.changeForm.formState.errors;
  const changing = vm.pending?.kind === 'change' ? vm.pending.release : null;
  const formErrors = ([['percentage', ids.percentage, 'platform.features.percentage'], ['organisations', ids.organisations, 'platform.features.organisations'],
    ['clinicalSignOff', ids.signOff, 'platform.features.signOff'], ['reason', ids.reason, 'platform.reason.label']] as const)
    .flatMap(([field, id, label]) => (e[field]?.message ? [{ id, label: t(label), message: errorText(e[field]?.message)! }] : []));
  const problem = vm.problem && t(vm.problem === 'locked' ? 'platform.features.locked' : 'platform.error.failed');

  return (
    <PageContainer>
      <PageHeader title={t('platform.features.title')} description={t('platform.features.lead')} />

      {vm.countries.length > 1 && vm.country && (
        <SegmentedControl label={t('platform.features.country')} value={vm.country} onValueChange={vm.onCountry}
          options={vm.countries.map(c => ({ value: c, label: c === 'IN' || c === 'AE' ? t(`platform.features.countryName.${c}`) : c }))} />
      )}

      {changing && vm.pending?.kind === 'change' && (
        <Card title={t('platform.features.changeTitle', { feature: name(changing.key), country: vm.pending.country })}>
          <form id={changeFormId} onSubmit={vm.onSave} noValidate className="grid gap-3 md:grid-cols-2">
            <ErrorSummary title={t('field.errorSummary')} focusKey={vm.changeForm.formState.submitCount} errors={formErrors} className="md:col-span-2" />
            <Controller control={vm.changeForm.control} name="stage" render={({ field }) => (
              <Select value={field.value} onValueChange={field.onChange}>
                <Field id={ids.stage} label={t('platform.features.col.stage')} required hint={t(`platform.features.stageHint.${field.value}`)}>
                  <SelectTrigger><SelectValue /></SelectTrigger>
                </Field>
                <SelectContent>{stages.map(s => <SelectItem key={s} value={s}>{t(`platform.features.stage.${s}`)}</SelectItem>)}</SelectContent>
              </Select>
            )} />
            {vm.stage === 'Percentage' && (
              <Field id={ids.percentage} label={t('platform.features.percentage')} required hint={t('platform.features.percentageHint')} error={errorText(e.percentage?.message)}>
                <Input inputMode="numeric" dir="ltr" autoComplete="off" {...vm.changeForm.register('percentage')} />
              </Field>
            )}
            {(vm.stage === 'Beta' || vm.stage === 'Percentage') && (
              <Field id={ids.organisations} label={t('platform.features.organisations')} required={vm.stage === 'Beta'} hint={t('platform.features.organisationsHint')}
                error={errorText(e.organisations?.message)} className="md:col-span-2">
                <Textarea rows={3} dir="ltr" autoComplete="off" {...vm.changeForm.register('organisations')} />
              </Field>
            )}
            {vm.stage && needsSignOff(changing, vm.stage) && (
              <Field id={ids.signOff} label={t('platform.features.signOff')} required hint={t('platform.features.signOffHint')} error={errorText(e.clinicalSignOff?.message)} className="md:col-span-2">
                <Input autoComplete="off" {...vm.changeForm.register('clinicalSignOff')} />
              </Field>
            )}
            <Field id={ids.reason} label={t('platform.reason.label')} required hint={t('platform.reason.hint')} error={errorText(e.reason?.message)} className="md:col-span-2">
              <Textarea rows={2} {...vm.changeForm.register('reason')} />
            </Field>
          </form>
        </Card>
      )}

      {vm.pending?.kind === 'off' && (
        <ReasonPanel title={t('platform.features.offTitle', { feature: name(vm.pending.release.key) })} form={vm.offForm} onConfirm={vm.onTurnOff}>
          <p className="text-sm">{t('platform.features.offBody')}</p>
        </ReasonPanel>
      )}

      <TableState state={vm.state} messages={{ loading: t('platform.state.loading'), error: t(vm.forbidden ? 'platform.state.forbidden' : 'platform.state.error'), empty: '' }}>
        {/* Stacked under 768 px with the feature first, so nothing scrolls sideways on a phone (MDV-P2-054). */}
        <Table label={t('platform.features.title')} stacked>
          <TableHeader>
            <TableRow>
              {featureColumns.map(c => <TableHead key={c} scope="col">{t(c)}</TableHead>)}
              {vm.canChange && <TableHead scope="col"><span className="sr-only">{t('platform.team.actions')}</span></TableHead>}
            </TableRow>
          </TableHeader>
          <TableBody>
            {vm.rows.map(r => (
              <TableRow key={r.key}>
                <TableHead scope="row" className="font-normal">
                  <p className="font-semibold">{name(r.key)}</p>
                  <p dir="ltr" className="font-mono text-sm text-muted-foreground">{r.key}</p>
                  <div className="mt-1 flex flex-wrap gap-1">
                    {r.clinical && <Tag tone="warning" size="sm" label={t('platform.features.clinical')} />}
                    {r.clinicSwitch && <Tag tone="neutral" size="sm" label={t('platform.features.clinicSwitch')} />}
                  </div>
                </TableHead>
                <TableCell label={t('platform.features.col.stage')}>
                  {r.alwaysOn
                    ? <span className="inline-flex items-center gap-1"><Lock aria-hidden className="size-4" />{t('platform.features.alwaysOn')}</span>
                    : <span className="inline-flex flex-wrap items-center gap-2">{stageText(r)}{r.isNew && <Tag tone="info" size="sm" label={t('platform.features.new')} />}</span>}
                </TableCell>
                <TableCell label={t('platform.features.col.signOff')}>
                  {signOffText(r)}
                </TableCell>
                <TableCell label={t('platform.features.col.changed')}>
                  {r.changedAt ? `${formatDate(new Date(r.changedAt))} ${formatTime(new Date(r.changedAt))}` : t('platform.features.never')}
                </TableCell>
                {vm.canChange && (
                  <TableCell>
                    {!r.alwaysOn && (
                      <div className="flex flex-wrap gap-2">
                        <Button variant="outline" aria-label={t('platform.features.changeFor', { feature: name(r.key) })} onClick={() => vm.onStartChange(r)}>
                          <SlidersHorizontal aria-hidden />{t('platform.features.change')}
                        </Button>
                        <Button variant="ghost" aria-label={t('platform.features.offFor', { feature: name(r.key) })} onClick={() => vm.onStartOff(r)}>
                          <Power aria-hidden />{t('platform.features.off')}
                        </Button>
                      </div>
                    )}
                  </TableCell>
                )}
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableState>

      {/* The screen's own actions, in the bottom bar (DESIGN.md Screen layout standard, MDV-P2-065). Without a change open there is none. */}
      {vm.pending?.kind === 'off' && (
        <ReasonBar confirmLabel={t('platform.features.offConfirm')} destructive busy={vm.busy} problem={problem} onCancel={vm.onCancel} />
      )}
      {vm.pending?.kind === 'change' && (
        <StickyActionBar status={problem && <p role="alert" className="text-sm font-semibold text-danger-700">{problem}</p>}>
          <Button type="button" variant="outline" onClick={vm.onCancel}>{t('platform.action.cancel')}</Button>
          <Button type="submit" form={changeFormId} loading={vm.busy}>{t('platform.features.save')}</Button>
        </StickyActionBar>
      )}
    </PageContainer>
  );
}
