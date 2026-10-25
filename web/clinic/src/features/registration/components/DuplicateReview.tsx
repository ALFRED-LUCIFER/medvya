import { Link } from 'react-router';
import { CircleCheck, UserSearch } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, buttonVariants, cn } from '@medvya/ui';
import type { IdentifierScheme } from '../../patients/domain/directory';
import type { DuplicateCandidate, DuplicateDecision } from '../domain/registration';

/** What the desk typed, as shown beside each possible match. */
export type NewEntry = { fullName: string; date: string; mobile: string; identifier: { scheme: IdentifierScheme; value: string } | null };

type Props = {
  candidates: DuplicateCandidate[];
  resolution: DuplicateDecision | null;
  onResolve: (decision: DuplicateDecision) => void;
  onReconsider: () => void;
  entry: NewEntry;
  recordPath: (id: string) => string;
};

/**
 * "Attention · possible existing record" (P-02 review): what the desk typed beside each possible match. The existing record
 * shows only initials, age, sex, MRN tail and masked IDs until "Use existing record" opens it (an audited read).
 */
export default function DuplicateReview({ candidates, resolution, onResolve, onReconsider, entry, recordPath }: Props) {
  const { t } = useI18n();
  const byName = candidates.some(c => c.matchedOnNameAndDateOfBirth);
  return (
    <section aria-labelledby="duplicate-review" className="flex flex-col gap-4 rounded-xl border bg-card p-4 shadow-[inset_4px_0_0_var(--color-warning-700)] rtl:shadow-[inset_-4px_0_0_var(--color-warning-700)]">
      <div className="flex items-start gap-2">
        <UserSearch className="mt-0.5 size-5 shrink-0 text-warning-700" aria-hidden />
        <div>
          <h2 id="duplicate-review" className="font-extrabold">{t('registration.duplicates.title')}</h2>
          <p className="text-sm text-muted-foreground">
            {t(byName ? 'registration.duplicates.matchedNameDob' : 'registration.duplicates.matchedId')} {t('registration.duplicates.phoneNever')}
          </p>
        </div>
      </div>

      <ul className="flex flex-col gap-3">
        {candidates.map(c => (
          <li key={c.patientId} className="grid gap-3 md:grid-cols-2">
            <div className="rounded-lg border p-3">
              <p className="text-xs font-bold tracking-wide text-muted-foreground uppercase">{t('registration.duplicates.new')}</p>
              <p className="font-bold"><bdi>{entry.fullName}</bdi></p>
              <p className="text-sm"><bdi dir="ltr">{entry.date}</bdi></p>
              <p className="text-sm"><bdi dir="ltr">{entry.mobile}</bdi></p>
              {entry.identifier && <p className="text-sm">{t(`patients.scheme.${entry.identifier.scheme}`)} <bdi dir="ltr">{entry.identifier.value}</bdi></p>}
            </div>
            <div className="rounded-lg border p-3">
              <p className="text-xs font-bold tracking-wide text-muted-foreground uppercase"><bdi>{t('registration.duplicates.existing', { mrn: c.mrnTail })}</bdi></p>
              <p className="font-bold">{t('registration.duplicates.summary', { initials: c.initials, age: c.ageYears, sex: t(`patients.sex.${c.sexAtBirth}`) })}</p>
              {c.identifiers.length === 0
                ? <p className="text-sm text-muted-foreground">{t('registration.duplicates.noIds')}</p>
                : c.identifiers.map(i => <p key={i.scheme} className="text-sm">{t(`patients.scheme.${i.scheme}`)} <bdi dir="ltr">{t('registration.duplicates.masked', { last: i.lastFour })}</bdi></p>)}
              <p className="mt-1 text-xs font-bold text-warning-700">
                {[c.matchedOnNameAndDateOfBirth && t('registration.duplicates.reason.nameDob'),
                  ...c.matchedIdentifiers.map(s => t('registration.duplicates.reason.id', { id: t(`patients.scheme.${s}`) }))].filter(Boolean).join(' · ')}
              </p>
              <Link to={recordPath(c.patientId)} className={cn(buttonVariants({ variant: 'outline' }), 'mt-2')}>{t('registration.duplicates.useExisting')}</Link>
            </div>
          </li>
        ))}
      </ul>

      {resolution ? (
        <div role="status" className="flex flex-wrap items-center gap-3">
          <p className="flex items-center gap-2 font-bold text-success-700">
            <CircleCheck className="size-5" aria-hidden />{t(resolution === 'MergeReview' ? 'registration.duplicates.decided.merge' : 'registration.duplicates.decided.notSame')}
          </p>
          <Button type="button" variant="ghost" onClick={onReconsider}>{t('registration.duplicates.change')}</Button>
        </div>
      ) : (
        <div className="flex flex-wrap gap-2">
          <Button type="button" variant="outline" onClick={() => onResolve('NotSamePerson')}>{t('registration.duplicates.notSame')}</Button>
          <Button type="button" variant="outline" onClick={() => onResolve('MergeReview')}>{t('registration.duplicates.mergeReview')}</Button>
        </div>
      )}
    </section>
  );
}
