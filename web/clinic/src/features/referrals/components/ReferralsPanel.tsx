import type { FormEventHandler } from 'react';
import { Link } from 'react-router';
import { Controller, type FieldError, type UseFormReturn } from 'react-hook-form';
import { CircleCheck, Send } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import {
  Button, buttonVariants, Input, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Table, TableBody, TableCell, TableHead, TableHeader, TableRow, TableState, Textarea,
} from '@medvya/ui';
import type { DocumentRow } from '../../documents/domain/documents';
import { describedBy, fieldError } from '../../settings/components/fieldStyles';
import { Field } from '../../settings/components/Field';
import { Section } from '../../settings/components/Section';
import {
  canSend, channels, evidenceOf, nextStates, referralColumns, sectionNames, urgencies,
  type AdvanceForm, type Channel, type FileInForm, type Referral, type ReferralForm, type ReferralProblem,
} from '../domain/referrals';
import { referralsHeading } from './ReferralJump';
import { stateIcons } from './stateIcons';

type Props = {
  encounterId: string;
  ended: boolean;
  referrals: Referral[];
  loading: boolean;
  failed: boolean;
  documents: DocumentRow[];
  createForm: UseFormReturn<ReferralForm>;
  fileForm: UseFormReturn<FileInForm>;
  advanceForm: UseFormReturn<AdvanceForm>;
  selected: Referral | null; onSelect: (id: string | null) => void;
  channel: Channel; onChannel: (channel: Channel) => void;
  problem: ReferralProblem | null;
  busy: boolean;
  onCreate: FormEventHandler<HTMLFormElement>;
  onFileIn: FormEventHandler<HTMLFormElement>;
  onSend: () => void;
  onAdvance: FormEventHandler<HTMLFormElement>;
};

/** Referrals (E-09): out from this consultation and received letters, each with its status in words and an icon, never colour alone. */
export default function ReferralsPanel(p: Props) {
  const { t, formatDate } = useI18n();
  const tableState = p.loading ? 'loading' : p.failed ? 'error' : p.referrals.length === 0 ? 'empty' : 'ready';
  const problem = (e: FieldError | undefined) => (e ? t(`referrals.error.${(e.type === 'server' ? 'failed' : 'required') as ReferralProblem}`) : null);
  const moving = p.advanceForm.watch('state');

  return (
    <div className="flex flex-col gap-4">
      <Section id={referralsHeading} title={t('referrals.title')} intro={t('referrals.intro')}>
        <TableState state={tableState} messages={{ loading: t('referrals.loading'), error: t('referrals.loadFailed'), empty: t('referrals.empty') }}>
          <Table label={t('referrals.tableLabel')} className="min-w-[640px]">
            <TableHeader>
              <TableRow>{referralColumns.map(c => <TableHead key={c} scope="col">{c === 'actions' ? <span className="sr-only">{t('referrals.col.actions')}</span> : t(`referrals.col.${c}`)}</TableHead>)}</TableRow>
            </TableHeader>
            <TableBody>
              {p.referrals.map(r => {
                const Icon = stateIcons[r.state];
                return (
                  <TableRow key={r.id}>
                    <TableHead scope="row">{r.provider}<span className="block text-sm font-normal text-muted-foreground">{t(`referrals.direction.${r.direction}`)}</span></TableHead>
                    <TableCell>{t(`referrals.urgency.${r.urgency}`)}</TableCell>
                    <TableCell><span className="inline-flex items-center gap-1.5 font-bold"><Icon aria-hidden className="size-4 shrink-0" />{t(`referrals.state.${r.state}`)}</span></TableCell>
                    <TableCell>{r.channel ? `${t(`referrals.channel.${r.channel}`)}${r.sentAt ? `, ${formatDate(new Date(r.sentAt))}` : ''}` : ''}</TableCell>
                    <TableCell>
                      {r.open && !p.ended && <Button type="button" variant="outline" className="min-h-11" aria-label={t('referrals.manageNamed', { name: r.provider })} onClick={() => p.onSelect(r.id)}>{t('referrals.manage')}</Button>}
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </TableState>
        {p.problem && <p role="alert" className={fieldError}>{t(`referrals.error.${p.problem}`)}</p>}
      </Section>

      {p.selected && !p.ended && (
        <Section title={t('referrals.manageTitle', { name: p.selected.provider })}>
          <ol className="flex flex-col gap-1" aria-label={t('referrals.history')}>
            {p.selected.history.map(s => (
              <li key={`${s.state}-${s.at}`}>
                <span className="font-bold">{t(`referrals.state.${s.state}`)}</span>{` ${formatDate(new Date(s.at))}`}{s.note && ` · ${s.note}`}
                {s.documentId && <span className="ms-2 text-sm text-muted-foreground">{t('referrals.evidence')}</span>}
              </li>
            ))}
          </ol>
          {canSend(p.selected) && (
            <div className="flex flex-wrap items-end gap-3">
              <Field id="ref-channel" label={t('referrals.field.channel')} hint={t('referrals.field.channel.hint')} className="min-w-64">
                <Select value={p.channel} onValueChange={v => p.onChannel(v as Channel)}>
                  <SelectTrigger id="ref-channel" className="h-11 w-full" {...describedBy('ref-channel', false, true)}><SelectValue /></SelectTrigger>
                  <SelectContent>{channels.map(c => <SelectItem key={c} value={c}>{t(`referrals.channel.${c}`)}</SelectItem>)}</SelectContent>
                </Select>
              </Field>
              <Button type="button" className="min-h-11" disabled={p.busy} onClick={p.onSend}><Send aria-hidden className="size-4" />{t('referrals.send')}</Button>
              <Link to={`/consult/${p.encounterId}/referrals/${p.selected.id}/letter`} className={buttonVariants({ variant: 'outline', className: 'min-h-11' })}>{t('referrals.letterLink')}</Link>
            </div>
          )}
          {nextStates(p.selected.state).length > 0 && p.selected.state !== 'Draft' && (
            <form noValidate onSubmit={p.onAdvance} className="grid gap-4 sm:grid-cols-2">
              <Field id="ref-state" label={t('referrals.field.state')}>
                <Controller control={p.advanceForm.control} name="state" render={({ field }) => (
                  <Select value={nextStates(p.selected!.state).includes(field.value) ? field.value : nextStates(p.selected!.state)[0]} onValueChange={field.onChange}>
                    <SelectTrigger id="ref-state" className="h-11 w-full"><SelectValue /></SelectTrigger>
                    <SelectContent>{nextStates(p.selected!.state).map(s => <SelectItem key={s} value={s}>{t(`referrals.state.${s}`)}</SelectItem>)}</SelectContent>
                  </Select>
                )} />
              </Field>
              {moving === 'ReportReceived' && (
                <Field id="ref-evidence" label={t('referrals.field.evidence')} hint={t('referrals.field.evidence.hint')}>
                  <Controller control={p.advanceForm.control} name="documentId" render={({ field }) => (
                    <Select value={field.value} onValueChange={field.onChange}>
                      <SelectTrigger id="ref-evidence" className="h-11 w-full" {...describedBy('ref-evidence', false, true)}><SelectValue placeholder={t('referrals.choose')} /></SelectTrigger>
                      <SelectContent>{p.documents.map(d => <SelectItem key={d.id} value={d.id}>{`${t(`documents.type.${d.type}`)}, ${d.documentDate}`}</SelectItem>)}</SelectContent>
                    </Select>
                  )} />
                </Field>
              )}
              <Field id="ref-note" label={t('referrals.field.note')} className="sm:col-span-2">
                <Input id="ref-note" className="h-11" autoComplete="off" maxLength={200} {...p.advanceForm.register('note')} />
              </Field>
              <Button type="submit" variant="outline" className="min-h-11 self-start" disabled={p.busy}>{t('referrals.update')}</Button>
            </form>
          )}
          {evidenceOf(p.selected).length > 0 && <p className="flex items-center gap-2 text-sm"><CircleCheck aria-hidden className="size-4" />{t('referrals.evidenceCount', { count: evidenceOf(p.selected).length })}</p>}
        </Section>
      )}

      {!p.ended && (
        <>
          <Section title={t('referrals.add.title')}>
            <form noValidate onSubmit={p.onCreate} className="grid gap-4 sm:grid-cols-2">
              <Field id="ref-provider" label={t('referrals.field.provider')} error={problem(p.createForm.formState.errors.provider)}>
                <Input id="ref-provider" className="h-11" autoComplete="off" {...describedBy('ref-provider', !!p.createForm.formState.errors.provider)} {...p.createForm.register('provider')} />
              </Field>
              <Field id="ref-urgency" label={t('referrals.field.urgency')}>
                <Controller control={p.createForm.control} name="urgency" render={({ field }) => (
                  <Select value={field.value ?? 'Routine'} onValueChange={field.onChange}>
                    <SelectTrigger id="ref-urgency" className="h-11 w-full"><SelectValue /></SelectTrigger>
                    <SelectContent>{urgencies.map(u => <SelectItem key={u} value={u}>{t(`referrals.urgency.${u}`)}</SelectItem>)}</SelectContent>
                  </Select>
                )} />
              </Field>
              <Field id="ref-reason" label={t('referrals.field.reason')} error={problem(p.createForm.formState.errors.reason)} className="sm:col-span-2">
                <Textarea id="ref-reason" rows={3} maxLength={500} {...describedBy('ref-reason', !!p.createForm.formState.errors.reason)} {...p.createForm.register('reason')} />
              </Field>
              <fieldset className="flex flex-col gap-2 sm:col-span-2">
                <legend className="mb-1 font-bold">{t('referrals.field.sections')}</legend>
                <p className="text-sm text-muted-foreground">{t('referrals.field.sections.hint')}</p>
                {sectionNames.map(s => (
                  <label key={s} className="flex min-h-11 cursor-pointer items-center gap-3 rounded-lg border p-3 has-[:focus-visible]:ring-[3px] has-[:focus-visible]:ring-ring/50">
                    <input type="checkbox" value={s} className="size-4" {...p.createForm.register('sections')} />
                    <span>{t(`referrals.section.${s}`)}</span>
                  </label>
                ))}
              </fieldset>
              <Button type="submit" className="min-h-11 self-start" disabled={p.busy}>{t('referrals.add.submit')}</Button>
            </form>
          </Section>

          <Section title={t('referrals.in.title')} intro={t('referrals.in.intro')}>
            <form noValidate onSubmit={p.onFileIn} className="grid gap-4 sm:grid-cols-2">
              <Field id="refin-provider" label={t('referrals.in.provider')} error={problem(p.fileForm.formState.errors.provider)}>
                <Input id="refin-provider" className="h-11" autoComplete="off" {...describedBy('refin-provider', !!p.fileForm.formState.errors.provider)} {...p.fileForm.register('provider')} />
              </Field>
              <Field id="refin-document" label={t('referrals.in.document')} hint={t('referrals.in.document.hint')} error={problem(p.fileForm.formState.errors.documentId)}>
                <Controller control={p.fileForm.control} name="documentId" render={({ field }) => (
                  <Select value={field.value} onValueChange={field.onChange}>
                    <SelectTrigger id="refin-document" className="h-11 w-full" {...describedBy('refin-document', !!p.fileForm.formState.errors.documentId, true)}><SelectValue placeholder={t('referrals.choose')} /></SelectTrigger>
                    <SelectContent>{p.documents.map(d => <SelectItem key={d.id} value={d.id}>{`${t(`documents.type.${d.type}`)}, ${d.documentDate}`}</SelectItem>)}</SelectContent>
                  </Select>
                )} />
              </Field>
              <Field id="refin-reason" label={t('referrals.in.reason')} className="sm:col-span-2">
                <Input id="refin-reason" className="h-11" autoComplete="off" maxLength={500} {...p.fileForm.register('reason')} />
              </Field>
              <Button type="submit" variant="outline" className="min-h-11 self-start" disabled={p.busy}>{t('referrals.in.submit')}</Button>
            </form>
          </Section>
        </>
      )}
    </div>
  );
}
