import type { FormEventHandler } from 'react';
import { Controller, type UseFormReturn } from 'react-hook-form';
import { CircleAlert, CircleCheck, CircleX, Send } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, Field, Input, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Table, TableBody, TableCell, TableHead, TableHeader, TableRow, TableState } from '@medvya/ui';
import { fieldError } from '../../settings/components/fieldStyles';
import { shareColumns, shareTargets, type DispensedForm, type RxShare, type ShareForm, type ShareProblem } from '../domain/share';

type Props = {
  shares: RxShare[]; loading: boolean; failed: boolean; timeZone: string;
  shareForm: UseFormReturn<ShareForm>; dispensedForm: UseFormReturn<DispensedForm>;
  problem: ShareProblem | null; sent: boolean; busy: boolean;
  onShare: FormEventHandler<HTMLFormElement>;
  dispensing: string | null; onDispensing: (id: string | null) => void; onDispensed: FormEventHandler<HTMLFormElement>; onCancelDispatch: (id: string) => void;
};

const stateIcon = { Sent: Send, Dispensed: CircleCheck, Cancelled: CircleX } as const;

/**
 * E-10 share (MDV-P2-020): send the authorised prescription as a secure link to the patient or a named pharmacy. The message names only the clinic;
 * the link opens with a code sent to the patient's registered mobile. A pharmacy share keeps its dispatch state, in words and an icon.
 */
export default function RxSharePanel(p: Props) {
  const { t, formatDate, formatTime } = useI18n();
  const target = p.shareForm.watch('target');
  const errors = p.shareForm.formState.errors;
  const when = (iso: string) => `${formatDate(new Date(iso), p.timeZone)} ${formatTime(new Date(iso), p.timeZone)}`;
  const state = p.loading ? 'loading' : p.failed ? 'error' : p.shares.length === 0 ? 'empty' : 'ready';
  return (
    <section aria-labelledby="rx-share" className="flex flex-col gap-3 rounded-xl border p-4 print:hidden">
      <h2 id="rx-share" className="font-extrabold">{t('rxShare.title')}</h2>
      <p className="text-sm">{t('rxShare.intro')}</p>
      <form className="flex flex-wrap items-end gap-3" onSubmit={p.onShare} noValidate>
        <Controller control={p.shareForm.control} name="target" render={({ field }) => (
          <Select value={field.value} onValueChange={field.onChange}>
            <Field id="rx-share-target" label={t('rxShare.to')} className="min-w-56">
              <SelectTrigger className="w-full"><SelectValue /></SelectTrigger>
            </Field>
            <SelectContent>{shareTargets.map(s => <SelectItem key={s} value={s}>{t(`rxShare.target.${s}`)}</SelectItem>)}</SelectContent>
          </Select>
        )} />
        {target === 'Pharmacy' && <>
          <Field id="rx-share-pharmacy" label={t('rxShare.pharmacyName')} error={errors.pharmacyName && t('rxShare.error.pharmacyName')} className="min-w-56">
            <Input {...p.shareForm.register('pharmacyName')} />
          </Field>
          <Field id="rx-share-email" label={t('rxShare.pharmacyEmail')} error={errors.pharmacyEmail && t('rxShare.error.pharmacyEmail')} className="min-w-56">
            <Input type="email" dir="ltr" {...p.shareForm.register('pharmacyEmail')} />
          </Field>
        </>}
        <Button type="submit" loading={p.busy}><Send aria-hidden className="size-4" />{t('rxShare.send')}</Button>
      </form>
      {p.sent && <p role="status" className="flex items-center gap-2"><CircleCheck aria-hidden className="size-4" />{t('rxShare.sent')}</p>}
      {p.problem && <p role="alert" className={`flex items-center gap-2 ${fieldError}`}><CircleAlert aria-hidden className="size-4" />{t(`rxShare.error.${p.problem}`)}</p>}

      <TableState state={state} messages={{ loading: t('rxShare.loading'), error: t('rxShare.loadFailed'), empty: t('rxShare.empty') }}>
        <Table label={t('rxShare.tableLabel')}>
          <TableHeader>
            <TableRow>{shareColumns.map(c => <TableHead key={c} scope="col">{c === 'actions' ? <span className="sr-only">{t('rxShare.col.actions')}</span> : t(`rxShare.col.${c}`)}</TableHead>)}</TableRow>
          </TableHeader>
          <TableBody>
            {p.shares.map(s => {
              const Icon = s.dispatch ? stateIcon[s.dispatch.state] : null;
              const name = s.pharmacyName ?? t(`rxShare.target.${s.channel === 'Sms' ? 'PatientSms' : 'PatientEmail'}`);
              return (
                <TableRow key={s.id}>
                  <TableHead scope="row">{name}<span className="block text-sm font-normal text-muted-foreground">{t('rxShare.version', { version: s.versionNo })}</span></TableHead>
                  <TableCell>{when(s.sharedAt)}<span className="block text-sm text-muted-foreground">{t('rxShare.expires', { when: when(s.expiresAt) })}</span></TableCell>
                  <TableCell>
                    {s.replaced && <span className="flex items-center gap-1.5 font-bold"><CircleAlert aria-hidden className="size-4 shrink-0" />{t('rxShare.replaced')}</span>}
                    {s.dispatch && Icon && <span className="flex items-center gap-1.5 font-bold"><Icon aria-hidden className="size-4 shrink-0" />{t(`rxShare.dispatch.${s.dispatch.state}`)}</span>}
                    {s.dispatch?.dispensedAt && <span className="block text-sm">{t('rxShare.dispensedAt', { when: when(s.dispatch.dispensedAt) })}{s.dispatch.pharmacyRef && ` · ${s.dispatch.pharmacyRef}`}</span>}
                  </TableCell>
                  <TableCell>
                    {s.dispatch?.state === 'Sent' && <div className="flex flex-wrap gap-2">
                      <Button type="button" variant="outline" aria-label={t('rxShare.markDispensedNamed', { name })} onClick={() => p.onDispensing(s.id)}>{t('rxShare.markDispensed')}</Button>
                      <Button type="button" variant="outline" aria-label={t('rxShare.cancelNamed', { name })} disabled={p.busy} onClick={() => p.onCancelDispatch(s.id)}>{t('rxShare.cancel')}</Button>
                    </div>}
                  </TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      </TableState>

      {p.dispensing && (
        <form className="flex flex-wrap items-end gap-3" aria-label={t('rxShare.dispensedTitle')} onSubmit={p.onDispensed} noValidate>
          <Field id="rx-dispensed-at" label={t('rxShare.dispensedWhen')} error={p.dispensedForm.formState.errors.dispensedAt && t('rxShare.error.dispensedAt')} className="min-w-56">
            <Input type="datetime-local" {...p.dispensedForm.register('dispensedAt')} />
          </Field>
          <Field id="rx-dispensed-ref" label={t('rxShare.pharmacyRef')} className="min-w-56">
            <Input dir="ltr" {...p.dispensedForm.register('pharmacyRef')} />
          </Field>
          <Button type="submit" loading={p.busy}>{t('rxShare.saveDispensed')}</Button>
          <Button type="button" variant="outline" onClick={() => p.onDispensing(null)}>{t('rxShare.close')}</Button>
        </form>
      )}
    </section>
  );
}
