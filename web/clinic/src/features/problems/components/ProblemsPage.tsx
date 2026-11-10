import type { FormEventHandler } from 'react';
import { Controller, type UseFormReturn } from 'react-hook-form';
import { Link } from 'react-router';
import { CircleCheck, CircleHelp, MessageCircle } from 'lucide-react';
import { ltr, useI18n } from '@medvya/i18n';
import { Button, buttonVariants, Input, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Table, TableBody, TableCell, TableHead, TableHeader, TableRow, TableState } from '@medvya/ui';
import type { ClinicalRefusal } from '../../clinical-access/domain/clinicalAccess';
import type { EncounterBanner } from '../../encounters/domain/encounter';
import IdentityBanner from '../../encounters/components/IdentityBanner';
import { Field } from '../../settings/components/Field';
import { describedBy } from '../../settings/components/fieldStyles';
import { statuses, verificationKey, verifications, type AddDraft, type CodeSet, type DiagnosisCode, type EditDraft, type Problem, type ProblemList, type ProblemRow } from '../domain/problems';

type Props = {
  view: ProblemList | null; banner: EncounterBanner | null; codeSet: CodeSet | null; loading: boolean; notFound: boolean; refusal: ClinicalRefusal | null; failed: boolean;
  addForm: UseFormReturn<AddDraft>; editForm: UseFormReturn<EditDraft>; search: string; results: DiagnosisCode[]; picked: DiagnosisCode | null;
  editing: string | null; problem: Problem | null; adding: boolean; saving: boolean; online: boolean;
  onSearch: (q: string) => void; onPick: (c: DiagnosisCode) => void; onClearPick: () => void; onAdd: FormEventHandler<HTMLFormElement>;
  onEdit: (r: ProblemRow) => void; onCancelEdit: () => void; onSaveEdit: FormEventHandler<HTMLFormElement>;
  consultPath: string; boardPath: string;
};

/**
 * Problems with ICD-10 codes and verification states (E-07, MDV-P2-010). Each state is written in words with its own icon; a provisional
 * problem also has a dashed outline, so it never reads as a confirmed diagnosis. Search is by code or words; a code is never guessed.
 */
export default function ProblemsPage(p: Props) {
  const { t } = useI18n();
  if (p.loading) return <p role="status">{t('problems.loading')}</p>;
  if (p.refusal) return <Message title={t('consult.refused.title')} body={t(p.refusal.canRequest ? 'consult.refused.ask' : 'consult.refused.role')} boardPath={p.boardPath} />;
  if (!p.view || p.notFound || p.failed) return <Message title={t(p.notFound ? 'consult.notFound' : 'problems.loadFailed')} boardPath={p.boardPath} />;
  const { view } = p;
  return (
    <div className="flex flex-col gap-4">
      {p.banner && <IdentityBanner banner={p.banner} />}
      <section aria-labelledby="problems-title" className="flex flex-col gap-3 rounded-xl border bg-card p-4">
        <h2 id="problems-title" className="text-lg font-extrabold">{t('problems.title')}</h2>
        {p.codeSet && <p className="text-sm text-muted-foreground">{t('problems.codeSet', { system: p.codeSet.system, version: p.codeSet.version })}</p>}
        <TableState state={view.rows.length === 0 ? 'empty' : 'ready'} messages={{ loading: t('problems.loading'), error: t('problems.loadFailed'), empty: t('problems.empty') }}>
          <Table label={t('problems.tableLabel')} className="min-w-[760px]">
            <TableHeader>
              <TableRow>{(['problem', 'code', 'verification', 'onset', 'status', 'action'] as const).map(c => <TableHead key={c} scope="col">{t(`problems.col.${c}`)}</TableHead>)}</TableRow>
            </TableHeader>
            <TableBody>
              {view.rows.map(r => p.editing === r.id ? <EditRow key={r.id} row={r} {...p} /> : (
                <TableRow key={r.id}>
                  <TableCell className="font-bold">{r.description}</TableCell>
                  <TableCell>{r.code ? <span>{ltr(r.code)}<span className="block text-sm text-muted-foreground">{ltr(`${r.codeSystem} ${r.codeSystemVersion}`)}</span></span> : <span className="text-muted-foreground">{t('problems.uncoded')}</span>}</TableCell>
                  <TableCell><VerificationTag row={r} /></TableCell>
                  <TableCell>{r.onsetOn ? ltr(r.onsetOn) : <span className="text-muted-foreground">{t('problems.onsetBlank')}</span>}</TableCell>
                  <TableCell>{t(`problems.status.${r.status}`)}</TableCell>
                  <TableCell>{!view.locked && <Button type="button" variant="outline" size="sm" className="min-h-11" disabled={!p.online} aria-label={t('problems.editFor', { name: r.description })} onClick={() => p.onEdit(r)}>{t('problems.edit')}</Button>}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableState>
        {view.locked ? <p role="status" className="font-bold">{t('problems.locked')}</p> : <AddForm {...p} />}
        {p.problem && <p role="alert" className="font-bold text-danger-700">{t(`problems.error.${p.problem}`)}</p>}
        <div><Link to={p.consultPath} className={buttonVariants({ variant: 'outline', className: 'min-h-11' })}>{t('problems.backToConsult')}</Link></div>
      </section>
    </div>
  );
}

/** The state in words with its own icon, never colour alone; provisional also gets a dashed outline. */
function VerificationTag({ row }: { row: ProblemRow }) {
  const { t } = useI18n();
  const Icon = row.verification === 'Verified' ? CircleCheck : row.verification === 'Provisional' ? CircleHelp : MessageCircle;
  const dashed = row.verification === 'Provisional' ? 'rounded-md border border-dashed border-foreground px-2 py-0.5' : '';
  return <span className={`inline-flex items-center gap-1.5 font-semibold ${dashed}`}><Icon aria-hidden className="size-4 shrink-0" />{t(`problems.verification.${verificationKey(row)}`)}</span>;
}

function AddForm(p: Props) {
  const { t } = useI18n();
  const { errors } = p.addForm.formState;
  return (
    <form noValidate onSubmit={p.onAdd} aria-labelledby="problems-add-title" className="flex flex-col gap-3 rounded-md border p-3">
      <h3 id="problems-add-title" className="font-extrabold">{t('problems.add.title')}</h3>
      {p.picked ? (
        <div className="flex flex-wrap items-center gap-2">
          <p role="status"><span className="font-bold">{ltr(p.picked.code)}</span>{` · ${p.picked.title}`}</p>
          <Button type="button" variant="outline" size="sm" className="min-h-11" onClick={p.onClearPick}>{t('problems.add.clearCode')}</Button>
        </div>
      ) : (
        <Field id="problem-search" label={t('problems.add.search')} hint={t('problems.add.searchHint')}>
          <Input id="problem-search" type="search" autoComplete="off" className="h-11" value={p.search} onChange={e => p.onSearch(e.target.value)} {...describedBy('problem-search', false, true)} />
          {p.search.trim() !== '' && (p.results.length === 0
            ? <p role="status" className="text-sm">{t('problems.add.noResults')}</p>
            : <ul aria-label={t('problems.add.results')} className="flex flex-col gap-1">
                {p.results.map(c => (
                  <li key={c.code}><Button type="button" variant="ghost" className="min-h-11 w-full justify-start text-start" onClick={() => p.onPick(c)}><span className="font-bold">{ltr(c.code)}</span>{` · ${c.title}`}</Button></li>
                ))}
              </ul>)}
        </Field>
      )}
      <div className="grid gap-3 sm:grid-cols-3">
        <Field id="problem-description" label={t('problems.add.description')} hint={t(p.picked ? 'problems.add.descriptionFromCode' : 'problems.add.descriptionHint')} error={errors.description ? t('problems.add.descriptionRequired') : null}>
          <Input id="problem-description" autoComplete="off" maxLength={200} className="h-11" placeholder={p.picked?.title} {...describedBy('problem-description', !!errors.description, true)} {...p.addForm.register('description')} />
        </Field>
        <Field id="problem-verification" label={t('problems.col.verification')}>
          <Controller control={p.addForm.control} name="verification" render={({ field }) => (
            <Select value={field.value} onValueChange={field.onChange}>
              <SelectTrigger id="problem-verification" className="h-11 w-full"><SelectValue /></SelectTrigger>
              <SelectContent>{verifications.map(v => <SelectItem key={v} value={v}>{t(`problems.verification.${v}`)}</SelectItem>)}</SelectContent>
            </Select>
          )} />
        </Field>
        <Field id="problem-onset" label={t('problems.col.onset')}>
          <Input id="problem-onset" type="date" className="h-11" {...p.addForm.register('onsetOn')} />
        </Field>
      </div>
      <div><Button type="submit" className="min-h-11" loading={p.adding} disabled={!p.online}>{t('problems.add.submit')}</Button></div>
    </form>
  );
}

function EditRow(p: Props & { row: ProblemRow }) {
  const { t } = useI18n();
  const { row } = p;
  return (
    <TableRow>
      <TableCell colSpan={6}>
        <form noValidate onSubmit={p.onSaveEdit} aria-label={t('problems.editFor', { name: row.description })} className="flex flex-col gap-3">
          <p className="font-bold">{row.code ? `${row.code} · ${row.description}` : row.description}</p>
          <div className="grid gap-3 sm:grid-cols-3">
            <Field id={`edit-verification-${row.id}`} label={t('problems.col.verification')}>
              <Controller control={p.editForm.control} name="verification" render={({ field }) => (
                <Select value={field.value} onValueChange={field.onChange}>
                  <SelectTrigger id={`edit-verification-${row.id}`} className="h-11 w-full"><SelectValue /></SelectTrigger>
                  <SelectContent>{verifications.map(v => <SelectItem key={v} value={v}>{t(`problems.verification.${v}`)}</SelectItem>)}</SelectContent>
                </Select>
              )} />
            </Field>
            <Field id={`edit-status-${row.id}`} label={t('problems.col.status')}>
              <Controller control={p.editForm.control} name="status" render={({ field }) => (
                <Select value={field.value} onValueChange={field.onChange}>
                  <SelectTrigger id={`edit-status-${row.id}`} className="h-11 w-full"><SelectValue /></SelectTrigger>
                  <SelectContent>{statuses.map(s => <SelectItem key={s} value={s}>{t(`problems.status.${s}`)}</SelectItem>)}</SelectContent>
                </Select>
              )} />
            </Field>
            <Field id={`edit-onset-${row.id}`} label={t('problems.col.onset')}>
              <Input id={`edit-onset-${row.id}`} type="date" className="h-11" {...p.editForm.register('onsetOn')} />
            </Field>
          </div>
          <div className="flex gap-2">
            <Button type="submit" className="min-h-11" loading={p.saving} disabled={!p.online}>{t('problems.save')}</Button>
            <Button type="button" variant="outline" className="min-h-11" onClick={p.onCancelEdit}>{t('problems.cancel')}</Button>
          </div>
        </form>
      </TableCell>
    </TableRow>
  );
}

function Message({ title, body, boardPath }: { title: string; body?: string; boardPath: string }) {
  const { t } = useI18n();
  return (
    <div role="alert" className="flex flex-col items-start gap-3 rounded-xl border bg-card p-6">
      <h1 className="text-lg font-extrabold">{title}</h1>
      {body && <p>{body}</p>}
      <Link to={boardPath} className={buttonVariants({ variant: 'outline', className: 'min-h-11' })}>{t('consult.backToBoard')}</Link>
    </div>
  );
}
