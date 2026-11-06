import type { ReactNode } from 'react';
import { Link } from 'react-router';
import { ArrowLeft, Check, GitMerge, Undo2, X } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, buttonVariants, cn, Table, TableBody, TableCaption, TableCell, TableHead, TableHeader, TableRow } from '@medvya/ui';
import { availableActions, type MergeAction, type MergeQueueRow, type Refusal, type MergeReview as Review, type MergeSide } from '../domain/merge';

type LoadState = 'loading' | 'ready' | 'invalid' | 'forbidden' | 'notFound' | 'error';

type Props = {
  queue: MergeQueueRow[];
  queueState: LoadState;
  review: Review | null;
  reviewState: LoadState | null;
  busy: boolean;
  /** Why the server refused the last step (e.g. "same_approver"), shown as translated text. */
  refusal: Refusal | null;
  can: { request: boolean; approve: boolean; undo: boolean };
  backPath: string;
  reviewPath: (id: string) => string;
  onDecide: (action: MergeAction) => void;
  onRetry: () => void;
};

const Ltr = ({ children }: { children: ReactNode }) => <bdi dir="ltr">{children}</bdi>;

const icons = { notSame: X, requestApproval: GitMerge, approve: Check, undo: Undo2 } as const;

/** P-03 merge review: the open requests and, for one, the two records side by side with the steps this role may take. */
export default function MergeReview(props: Props) {
  const { review, queue } = props;
  const { t, formatDate } = useI18n();
  const date = (iso: string) => formatDate(new Date(`${iso.slice(0, 10)}T00:00:00`));
  const actions = review ? availableActions(review, props.can) : [];
  const loadError = props.queueState === 'error' || props.reviewState === 'error';
  const status = props.queueState === 'loading' || props.reviewState === 'loading' ? t('merge.loading')
    : props.queueState === 'forbidden' || props.reviewState === 'forbidden' ? t('err.403')
    : props.reviewState === 'notFound' ? t('merge.notFound') : loadError ? t('merge.error') : props.refusal ? t(`merge.refused.${props.refusal}`) : '';

  const rows: [string, (s: MergeSide) => ReactNode][] = [
    [t('merge.field.name'), s => <bdi>{s.fullName}</bdi>],
    [t('merge.field.mrn'), s => <Ltr>{s.mrn}</Ltr>],
    [t('merge.field.dob'), s => date(s.dateOfBirth)],
    [t('merge.field.sex'), s => t(`patients.sex.${s.sexAtBirth}`)],
    [t('merge.field.mobile'), s => <Ltr>{s.mobile}</Ltr>],
    [t('merge.field.identifiers'), s => s.identifiers.length === 0 ? <span className="text-muted-foreground">{t('patients.record.none')}</span>
      : <ul>{s.identifiers.map(i => <li key={`${i.scheme}${i.lastFour}`}>{t(`patients.scheme.${i.scheme}`)} <Ltr>{t('patients.masked', { last: i.lastFour })}</Ltr></li>)}</ul>],
    [t('merge.field.visits'), s => s.visits],
  ];

  return (
    <div className="flex max-w-[960px] flex-col gap-4">
      <Link to={props.backPath} className={cn(buttonVariants({ variant: 'ghost' }), 'self-start px-2')}>
        <ArrowLeft className="rtl:rotate-180" aria-hidden />{t('patients.record.back')}
      </Link>
      <h1 className="text-2xl font-extrabold">{t('merge.title')}</h1>
      <p role="status" className={cn('text-sm', props.refusal || loadError ? 'font-semibold text-danger-700' : 'text-muted-foreground')}>{status}</p>
      {loadError && <Button variant="outline" className="self-start" onClick={props.onRetry}>{t('patients.retry')}</Button>}

      <section aria-labelledby="merge-queue" className="rounded-xl border bg-card p-4">
        <h2 id="merge-queue" className="mb-2 text-base font-extrabold">{t('merge.queue')}</h2>
        {props.queueState === 'ready' && queue.length === 0 ? <p className="text-muted-foreground">{t('merge.queue.empty')}</p> : (
          <ul className="flex flex-col gap-1">
            {queue.map(r => (
              <li key={r.id}>
                <Link to={props.reviewPath(r.id)} aria-current={review?.id === r.id ? 'page' : undefined}
                  className={cn(buttonVariants({ variant: 'ghost' }), 'h-auto min-h-11 w-full flex-wrap justify-start gap-x-3 whitespace-normal py-2', review?.id === r.id && 'bg-muted')}>
                  <span>{t('merge.queue.row', { survivor: r.survivorMrn, merged: r.mergedMrn })}</span>
                  <span className="text-xs font-bold text-muted-foreground">{t(`merge.status.${r.status}`)}</span>
                </Link>
              </li>
            ))}
          </ul>
        )}
      </section>

      {review && (
        <section aria-labelledby="merge-compare" className="flex flex-col gap-3 rounded-xl border bg-card p-4">
          <h2 id="merge-compare" className="text-base font-extrabold">{t('merge.compare')} <span className="text-sm font-bold text-muted-foreground">{t(`merge.status.${review.status}`)}</span></h2>
          <p className="text-sm text-muted-foreground">{t('merge.explain')}</p>
          <Table className="table-fixed" containerClassName="overflow-visible rounded-none border-0">
            <TableCaption className="sr-only">{t('merge.compare')}</TableCaption>
            <TableHeader className="bg-transparent">
              <TableRow className="border-b-0">
                <TableHead scope="col" className="p-2 whitespace-normal"><span className="sr-only">{t('merge.field.name')}</span></TableHead>
                <TableHead scope="col" className="p-2 whitespace-normal">{t('merge.survivor')}</TableHead>
                <TableHead scope="col" className="p-2 whitespace-normal">{t('merge.merged')}</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {rows.map(([label, cell]) => (
                <TableRow key={label} className="border-t border-b-0">
                  <TableHead scope="row" className="h-auto w-[28%] p-2 text-xs font-bold break-words whitespace-normal text-muted-foreground">{label}</TableHead>
                  <TableCell className="p-2 align-top break-words">{cell(review.survivor)}</TableCell>
                  <TableCell className="p-2 align-top break-words">{cell(review.merged)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          <p className="text-sm">{t('merge.approvals', { done: review.approvals, needed: review.approvalsRequired })}</p>
          {review.approvalsRequired > 1 && <p className="text-sm text-muted-foreground">{t('merge.secondApprover')}</p>}
          {actions.length === 0 ? <p className="text-sm text-muted-foreground">{t(`merge.closed.${review.status}`)}</p> : (
            <div className="flex flex-wrap gap-2">
              {actions.map(a => {
                const Icon = icons[a];
                return <Button key={a} variant={a === 'approve' ? 'default' : 'outline'} disabled={props.busy} onClick={() => props.onDecide(a)}><Icon aria-hidden />{t(`merge.action.${a}`)}</Button>;
              })}
            </div>
          )}
        </section>
      )}
    </div>
  );
}
