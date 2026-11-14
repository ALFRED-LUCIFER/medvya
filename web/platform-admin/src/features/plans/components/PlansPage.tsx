import { useId, type FormEventHandler } from 'react';
import { History, Pencil } from 'lucide-react';
import { useI18n, type MessageKey } from '@medvya/i18n';
import { Button, Card, ChoiceRow, ErrorSummary, Field, Input, PageContainer, PageHeader, StickyActionBar, Table, TableBody, TableCell, TableHead, TableHeader, TableRow, TableState, Textarea } from '@medvya/ui';
import type { UseFormReturn } from 'react-hook-form';
import { assistKey, featureChoices, featureLabel, historyColumns, planColumns, planName, type Plan, type PlanForm, type PlanPrice, type PlanVersion } from '../domain/plans';

type Props = {
  vm: {
    state: 'loading' | 'error' | 'empty' | 'ready'; forbidden: boolean; plans: Plan[]; canEdit: boolean;
    editing: PlanVersion | null; form: UseFormReturn<PlanForm>; busy: boolean; stale: boolean; failed: boolean;
    onEdit: (v: PlanVersion) => void; onCancel: () => void; onSave: FormEventHandler; onReload: () => void;
    saved: PlanVersion | null; onDismissSaved: () => void;
    history: Plan | null; onShowHistory: (code: string) => void; onHideHistory: () => void;
  };
};

const formId = 'plan-edit';

/** Plans and entitlements (design: Medvya Admin, Plans; ADR-019): prices per country, limits, trial and features. A save is a new version. */
export default function PlansPage({ vm }: Props) {
  const { t, formatDate, formatMoney } = useI18n();
  const id = useId();
  const name = (code: string) => { const k = planName(code); return k ? t(k) : code; };
  const featureText = (key: string) => { const k = featureLabel(key); return k ? t(k) : key; };
  const limit = (n: number | null) => (n === null ? t('platform.plans.unlimited') : String(n));
  const money = (minor: number | null, p: PlanPrice) => (minor === null ? null : formatMoney(minor, p.currency));

  /** A country's price in words: per month and per year (per doctor on Clinic), or Hospital's contract price from a yearly amount. */
  const priceLines = (v: PlanVersion, p: PlanPrice) => {
    const unit = v.perDoctor ? 'doctor' : 'practice';
    const lines = v.custom
      ? [t('site.pricing.custom'), money(p.yearly, p) && t('site.pricing.from', { price: money(p.yearly, p)! })]
      : [money(p.monthly, p) && `${money(p.monthly, p)} ${t(`site.pricing.unit.${unit}.month`)}`, money(p.yearly, p) && `${money(p.yearly, p)} ${t(`site.pricing.unit.${unit}.year`)}`];
    const shown = lines.filter(Boolean) as string[];
    if (p.assistMonthly !== null) shown.push(t('platform.plans.assistAddOn', { price: money(p.assistMonthly, p)! }));
    return shown.length ? shown : [t('site.pricing.noPrice')];
  };
  const price = (v: PlanVersion, country: string) => v.prices.find(p => p.country === country);

  const e = vm.form.formState.errors;
  const errorText = (code?: string) => (code ? t(`platform.error.${code}` as MessageKey) : undefined);
  const editErrors = vm.editing ? [
    ...vm.editing.prices.flatMap((p, i) => (['monthly', 'yearly', 'assistMonthly'] as const).flatMap(f => (e.prices?.[i]?.[f]?.message
      ? [{ id: `${id}-${i}-${f}`, label: `${t(`platform.plans.country.${p.country}`)} · ${t(`platform.plans.price.${f}`)}`, message: errorText(e.prices[i][f].message)! }] : []))),
    ...(['doctors', 'staff', 'locations', 'assistVisitsPerDoctorMonth', 'trialDays', 'reason'] as const)
      .flatMap(f => (e[f]?.message ? [{ id: `${id}-${f}`, label: t(`platform.plans.field.${f}`), message: errorText(e[f]?.message)! }] : [])),
  ] : [];

  return (
    <PageContainer>
      <PageHeader title={t('platform.plans.title')} description={t('platform.plans.lead')} />

      {vm.saved && (
        <p role="status" className="font-semibold">
          {t('platform.plans.savedNotice', { plan: name(vm.saved.code), version: vm.saved.version })}
        </p>
      )}

      {vm.editing && (
        <Card title={t('platform.plans.editTitle', { plan: name(vm.editing.code), version: vm.editing.version + 1 })}>
          <p className="text-sm">{t('platform.plans.editBody')}</p>
          <form id={formId} onSubmit={vm.onSave} noValidate className="flex flex-col gap-4">
            <ErrorSummary title={t('field.errorSummary')} focusKey={vm.form.formState.submitCount} errors={editErrors} />
            <p id={`${id}-price-hint`} className="text-caption text-muted-foreground">{t('platform.plans.priceHint')}</p>
            {vm.editing.prices.map((p, i) => (
              <fieldset key={p.country} aria-describedby={`${id}-price-hint`} className="grid gap-3 md:grid-cols-3">
                <legend className="mb-2 text-label">{t(`platform.plans.country.${p.country}`)}</legend>
                {(['monthly', 'yearly', 'assistMonthly'] as const).map(f => (
                  <Field key={f} id={`${id}-${i}-${f}`} label={t(`platform.plans.price.${f}`)} unit={p.currency} hint={f === 'assistMonthly' ? t('platform.plans.assistAddOnHint') : undefined}
                    error={errorText(e.prices?.[i]?.[f]?.message)}>
                    <Input inputMode="decimal" dir="ltr" autoComplete="off" {...vm.form.register(`prices.${i}.${f}`)} />
                  </Field>
                ))}
              </fieldset>
            ))}
            <fieldset aria-describedby={`${id}-limit-hint`} className="grid gap-3 md:grid-cols-3">
              <legend className="mb-1 text-label">{t('platform.plans.limits')}</legend>
              <p id={`${id}-limit-hint`} className="text-caption text-muted-foreground md:col-span-3">{t('platform.plans.unlimitedHint')}</p>
              {(['doctors', 'staff', 'locations', 'assistVisitsPerDoctorMonth'] as const).map(f => (
                <Field key={f} id={`${id}-${f}`} label={t(`platform.plans.field.${f}`)} error={errorText(e[f]?.message)}>
                  <Input inputMode="numeric" dir="ltr" autoComplete="off" {...vm.form.register(f)} />
                </Field>
              ))}
              <Field id={`${id}-trialDays`} label={t('platform.plans.field.trialDays')} required hint={t('platform.plans.trialHint')} error={errorText(e.trialDays?.message)}>
                <Input inputMode="numeric" dir="ltr" autoComplete="off" {...vm.form.register('trialDays')} />
              </Field>
            </fieldset>
            <fieldset>
              <legend className="mb-1 text-label">{t('platform.plans.features')}</legend>
              <p className="mb-2 text-caption text-muted-foreground">{t('platform.plans.featuresHint')}</p>
              <div className="grid md:grid-cols-2">
                {featureChoices(vm.editing).map(f => (
                  <ChoiceRow key={f} type="checkbox" value={f} {...vm.form.register('features')}>{featureText(f)}</ChoiceRow>
                ))}
              </div>
            </fieldset>
            <Field id={`${id}-reason`} label={t('platform.reason.label')} required hint={t('platform.reason.hint')} error={errorText(e.reason?.message)}>
              <Textarea rows={2} {...vm.form.register('reason')} />
            </Field>
          </form>
        </Card>
      )}

      <TableState state={vm.state} messages={{ loading: t('platform.state.loading'), error: t(vm.forbidden ? 'platform.state.forbidden' : 'platform.state.error'), empty: t('platform.plans.empty') }}>
        {/* Stacked under 768 px with the plan first, so no price scrolls out of sight on a phone (MDV-P2-054). */}
        <Table label={t('platform.plans.title')} stacked>
          <TableHeader>
            <TableRow>
              {planColumns.map(c => <TableHead key={c} scope="col">{t(c)}</TableHead>)}
              <TableHead scope="col"><span className="sr-only">{t('platform.plans.actions')}</span></TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {vm.plans.map(({ code, current: v }) => (
              <TableRow key={code}>
                <TableHead scope="row" className="font-normal">
                  <p className="font-semibold">{name(code)}</p>
                  <p className="text-sm text-muted-foreground">{t('platform.plans.versionOn', { version: v.version, date: formatDate(new Date(v.createdAt)) })}</p>
                </TableHead>
                {(['IN', 'AE'] as const).map(c => (
                  <TableCell key={c} label={t(`platform.plans.country.${c}`)}>
                    {price(v, c) ? priceLines(v, price(v, c)!).map(line => <p key={line}>{line}</p>) : t('site.pricing.noPrice')}
                  </TableCell>
                ))}
                <TableCell label={t('platform.plans.limits')}>
                  <p>{t('platform.plans.limit.doctors', { value: v.perDoctor && v.doctors === null ? t('platform.plans.perDoctor') : limit(v.doctors) })}</p>
                  <p>{t('platform.plans.limit.staff', { value: limit(v.staff) })}</p>
                  <p>{t('platform.plans.limit.locations', { value: limit(v.locations) })}</p>
                </TableCell>
                <TableCell label={t('platform.plans.assist')}>
                  <p>{t(v.features.includes(assistKey) ? 'platform.plans.assistIncluded' : v.prices.some(p => p.assistMonthly !== null) ? 'platform.plans.assistAddOnShort' : 'platform.plans.assistNone')}</p>
                  <p className="text-sm text-muted-foreground">{v.assistVisitsPerDoctorMonth === null ? t('platform.plans.assistNoLimit') : t('platform.plans.assistQuota', { count: v.assistVisitsPerDoctorMonth })}</p>
                </TableCell>
                <TableCell label={t('platform.plans.trial')}>{v.trialDays ? t('platform.plans.trialDays', { count: v.trialDays }) : t('platform.plans.noTrial')}</TableCell>
                <TableCell label={t('platform.plans.features')}>{v.features.length ? v.features.map(featureText).join(', ') : t('platform.plans.noFeatures')}</TableCell>
                <TableCell>
                  <div className="flex flex-wrap gap-2">
                    {vm.canEdit && (
                      <Button variant="outline" aria-label={t('platform.plans.editFor', { plan: name(code) })} onClick={() => vm.onEdit(v)}><Pencil aria-hidden />{t('platform.plans.edit')}</Button>
                    )}
                    <Button variant="ghost" aria-label={t('platform.plans.historyFor', { plan: name(code) })} onClick={() => vm.onShowHistory(code)}><History aria-hidden />{t('platform.plans.history')}</Button>
                  </div>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableState>

      {vm.history && (
        <Card title={t('platform.plans.historyTitle', { plan: name(vm.history.code) })}>
          <Table label={t('platform.plans.historyTitle', { plan: name(vm.history.code) })} stacked>
            <TableHeader>
              <TableRow>{historyColumns.map(c => <TableHead key={c} scope="col">{t(c)}</TableHead>)}</TableRow>
            </TableHeader>
            <TableBody>
              {vm.history.history.map(v => (
                <TableRow key={v.id}>
                  <TableHead scope="row">{t('platform.plans.versionN', { version: v.version })}</TableHead>
                  <TableCell label={t('platform.plans.saved')}><time dateTime={v.createdAt}>{formatDate(new Date(v.createdAt))}</time></TableCell>
                  <TableCell label={t('platform.plans.by')}>{v.createdBy ?? t('platform.plans.approvedList')}</TableCell>
                  <TableCell label={t('platform.plans.reason')}>{v.reason ?? t('platform.audit.noReason')}</TableCell>
                  {(['IN', 'AE'] as const).map(c => (
                    <TableCell key={c} label={t(`platform.plans.country.${c}`)}>{price(v, c) ? priceLines(v, price(v, c)!).join(' · ') : t('site.pricing.noPrice')}</TableCell>
                  ))}
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Card>
      )}

      {/* The screen's own actions, in the bottom bar (DESIGN.md Screen layout standard, MDV-P2-065). */}
      {vm.editing ? (
        <StickyActionBar key="edit" status={(vm.stale || vm.failed) && (
          <p role="alert" className="text-sm font-semibold text-danger-700">{t(vm.stale ? 'platform.plans.stale' : 'platform.error.failed')}</p>
        )}>
          <Button type="button" variant="outline" onClick={vm.onCancel}>{t('platform.action.cancel')}</Button>
          {vm.stale && <Button type="button" variant="outline" onClick={vm.onReload}>{t('platform.plans.reload')}</Button>}
          <Button type="submit" form={formId} loading={vm.busy}>{t('platform.plans.saveVersion')}</Button>
        </StickyActionBar>
      ) : vm.history ? (
        <StickyActionBar key="history">
          <Button type="button" variant="outline" onClick={vm.onHideHistory}>{t('platform.plans.closeHistory')}</Button>
        </StickyActionBar>
      ) : vm.saved && (
        <StickyActionBar key="saved">
          <Button type="button" variant="outline" onClick={vm.onDismissSaved}>{t('platform.plans.done')}</Button>
        </StickyActionBar>
      )}
    </PageContainer>
  );
}
