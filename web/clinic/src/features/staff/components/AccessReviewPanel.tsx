import { CircleCheck, Clock } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button } from '@medvya/ui';
import { fieldError } from '../../settings/components/fieldStyles';
import { Section } from '../../settings/components/Section';
import { remaining, type AccessReview } from '../domain/accessReview';

type Props = { review: AccessReview | null; loading: boolean; failed: boolean; confirmingId: string | null; confirmFailed: boolean; onConfirm: (membershipId: string) => void };

/** Quarterly access review (MDV-P1-010): confirm each person's access, or offboard them in the staff list; completion is recorded. */
export default function AccessReviewPanel(p: Props) {
  const { t, formatDate } = useI18n();
  if (p.loading) return <p role="status">{t('settings.loading')}</p>;
  if (p.failed || !p.review) return <p role="alert" className={fieldError}>{t('settings.loadFailed')}</p>;
  const { review } = p;
  const open = remaining(review);
  return (
    <Section title={t('accessReview.title', { quarter: review.quarter })} intro={t('accessReview.intro')}>
      <p role="status" className="flex items-center gap-2 font-bold">
        {review.completedAt
          ? <><CircleCheck aria-hidden className="size-5" />{t('accessReview.completed', { date: formatDate(new Date(review.completedAt)) })}</>
          : <><Clock aria-hidden className="size-5" />{t('accessReview.open', { count: open })}</>}
      </p>
      <p className="text-sm text-muted-foreground">{review.lastCompletedAt ? t('accessReview.last', { date: formatDate(new Date(review.lastCompletedAt)) }) : t('accessReview.never')}</p>
      <ul className="flex flex-col divide-y rounded-lg border">
        {review.members.map(m => (
          <li key={m.membershipId} className="flex flex-wrap items-center justify-between gap-3 p-3">
            <span className="flex min-w-0 flex-col">
              <strong>{m.name ?? t('accessReview.unnamed')}</strong>
              <span className="text-sm text-muted-foreground">
                {t(`role.${m.role}`)}{' · '}{m.facilityName}{' · '}{m.lastSignIn ? t('accessReview.lastSignIn', { date: formatDate(new Date(m.lastSignIn)) }) : t('accessReview.neverSignedIn')}
              </span>
            </span>
            {m.decision
              ? <span className="inline-flex items-center gap-1.5 font-bold"><CircleCheck aria-hidden className="size-4" />{t(`accessReview.decision.${m.decision}`)}</span>
              : <Button variant="outline" loading={p.confirmingId === m.membershipId} aria-label={t('accessReview.confirmFor', { name: m.name ?? t('accessReview.unnamed') })}
                  onClick={() => p.onConfirm(m.membershipId)}>{t('accessReview.confirm')}</Button>}
          </li>
        ))}
      </ul>
      <p className="text-sm text-muted-foreground">{t('accessReview.offboardNote')}</p>
      {p.confirmFailed && <p role="alert" className={fieldError}>{t('settings.saveFailed')}</p>}
    </Section>
  );
}
