import type { FormEventHandler } from 'react';
import { type UseFormReturn } from 'react-hook-form';
import { Link } from 'react-router';
import { CircleCheck, CircleHelp, MessageCircle, type LucideIcon } from 'lucide-react';
import { ltr, useI18n } from '@medvya/i18n';
import { Button, buttonVariants, Card, Field, PageContainer, PageHeader, PageMessage, Input, Table, TableBody, TableCell, TableHead, TableHeader, StickyActionBar, TableRow, TableState, Tag, type Tone } from '@medvya/ui';
import type { ClinicalRefusal } from '../../clinical-access/domain/clinicalAccess';
import type { EncounterBanner } from '../../encounters/domain/encounter';
import PatientStrip from '../../encounters/components/PatientStrip';
import ProblemAddForm, { ProblemAddSubmit } from './ProblemAddForm';
import { problemColumns, statuses, verificationKey, verifications, type AddDraft, type CodeSet, type DiagnosisCode, type EditDraft, type Problem, type ProblemList, type ProblemRow } from '../domain/problems';
import { SelectField } from '../../forms/components/SelectField';

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
const addFormId = 'problems-add';

export default function ProblemsPage(p: Props) {
  const { t } = useI18n();
  if (p.loading) return <p role="status">{t('problems.loading')}</p>;
  const back = <Link to={p.boardPath} className={buttonVariants({ variant: 'outline' })}>{t('consult.backToBoard')}</Link>;
  if (p.refusal) return (
    <PageContainer role="alert">
      <PageHeader title={t('consult.refused.title')} actions={back} />
      <PageMessage kind="denied">{t(p.refusal.canRequest ? 'consult.refused.ask' : 'consult.refused.role')}</PageMessage>
    </PageContainer>
  );
  if (!p.view || p.notFound || p.failed) return <PageContainer role="alert"><PageHeader title={t(p.notFound ? 'consult.notFound' : 'problems.loadFailed')} actions={back} /></PageContainer>;
  const { view } = p;
  return (
    <PageContainer>
      {p.banner && <PatientStrip banner={p.banner} headingLevel={1} />}
      <Card title={t('problems.title')} intro={p.codeSet && t('problems.codeSet', { system: p.codeSet.system, version: p.codeSet.version })}>
        <TableState state={view.rows.length === 0 ? 'empty' : 'ready'} messages={{ loading: t('problems.loading'), error: t('problems.loadFailed'), empty: t('problems.empty') }}>
          <Table label={t('problems.tableLabel')} stacked className="md:min-w-[760px]">
            <TableHeader>
              <TableRow>{problemColumns.map(c => <TableHead key={c} scope="col">{t(`problems.col.${c}`)}</TableHead>)}</TableRow>
            </TableHeader>
            <TableBody>
              {view.rows.map(r => p.editing === r.id ? <EditRow key={r.id} row={r} {...p} /> : (
                <TableRow key={r.id}>
                  <TableHead scope="row" className="whitespace-normal">{r.description}</TableHead>
                  <TableCell label={t('problems.col.code')}>{r.code ? <span>{ltr(r.code)}<span className="block text-sm text-muted-foreground">{ltr(`${r.codeSystem} ${r.codeSystemVersion}`)}</span></span> : <span className="text-muted-foreground">{t('problems.uncoded')}</span>}</TableCell>
                  <TableCell label={t('problems.col.verification')}><VerificationTag row={r} /></TableCell>
                  <TableCell label={t('problems.col.onset')}>{r.onsetOn ? ltr(r.onsetOn) : <span className="text-muted-foreground">{t('problems.onsetBlank')}</span>}</TableCell>
                  <TableCell label={t('problems.col.status')}>{t(`problems.status.${r.status}`)}</TableCell>
                  <TableCell>{!view.locked && <Button type="button" variant="outline" size="sm" disabled={!p.online} aria-label={t('problems.editFor', { name: r.description })} onClick={() => p.onEdit(r)}>{t('problems.edit')}</Button>}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableState>
        {view.locked ? <p role="status" className="font-bold">{t('problems.locked')}</p> : <ProblemAddForm {...p} formId={addFormId} />}
        {p.problem && <p role="alert" className="font-bold text-danger-700">{t(`problems.error.${p.problem}`)}</p>}
      </Card>
      {/* The bottom bar (MDV-P2-064): Add problem submits the form above. */}
      <StickyActionBar>
        {!view.locked && <ProblemAddSubmit formId={addFormId} adding={p.adding} online={p.online} />}
        <Link to={p.consultPath} className={buttonVariants({ variant: 'outline' })}>{t('problems.backToConsult')}</Link>
      </StickyActionBar>
    </PageContainer>
  );
}

const verificationLooks = {
  Verified: { icon: CircleCheck, tone: 'success' }, PatientReported: { icon: MessageCircle, tone: 'neutral' }, Provisional: { icon: CircleHelp, tone: 'warning' },
} as const satisfies Record<ProblemRow['verification'], { icon: LucideIcon; tone: Exclude<Tone, 'ai'> }>;

/** The state as the library Tag (MDV-P2-060): words with its own icon, never colour alone; provisional keeps its dashed outline. */
function VerificationTag({ row }: { row: ProblemRow }) {
  const { t } = useI18n();
  return <Tag {...verificationLooks[row.verification]} label={t(`problems.verification.${verificationKey(row)}`)} className={row.verification === 'Provisional' ? 'border-dashed' : undefined} />;
}

function EditRow(p: Props & { row: ProblemRow }) {
  const { t } = useI18n();
  const { row } = p;
  return (
    <TableRow>
      <TableCell colSpan={problemColumns.length}>
        <form noValidate onSubmit={p.onSaveEdit} aria-label={t('problems.editFor', { name: row.description })} className="flex flex-col gap-3">
          <p className="font-bold">{row.code ? `${row.code} · ${row.description}` : row.description}</p>
          <div className="grid gap-3 md:grid-cols-3">
            <SelectField control={p.editForm.control} name="verification" id={`edit-verification-${row.id}`} label={t('problems.col.verification')}
              options={verifications} optionLabel={v => t(`problems.verification.${v}`)} />
            <SelectField control={p.editForm.control} name="status" id={`edit-status-${row.id}`} label={t('problems.col.status')}
              options={statuses} optionLabel={s => t(`problems.status.${s}`)} />
            <Field id={`edit-onset-${row.id}`} label={t('problems.col.onset')}>
              <Input type="date" {...p.editForm.register('onsetOn')} />
            </Field>
          </div>
          <div className="flex gap-2">
            <Button type="submit" loading={p.saving} disabled={!p.online}>{t('problems.save')}</Button>
            <Button type="button" variant="outline" onClick={p.onCancelEdit}>{t('problems.cancel')}</Button>
          </div>
        </form>
      </TableCell>
    </TableRow>
  );
}
