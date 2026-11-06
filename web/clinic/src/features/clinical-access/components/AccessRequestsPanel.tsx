import { Inbox } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@medvya/ui';
import { fieldError } from '../../settings/components/fieldStyles';
import { Section } from '../../settings/components/Section';
import type { DecideError, PendingAccessRequest } from '../domain/clinicalAccess';

type Props = {
  requests: PendingAccessRequest[]; grantDays: number[]; loading: boolean; failed: boolean;
  daysFor: (id: string) => number; onDays: (id: string, days: number) => void;
  decidingId: string | null; decideError: DecideError | null;
  onGrant: (id: string) => void; onDecline: (id: string) => void;
};

/** Access requests (MDV-P2-024): a clinician asked to open one patient's record; grant for a fixed time or decline. */
export default function AccessRequestsPanel(p: Props) {
  const { t, tCount, formatDate } = useI18n();
  if (p.loading) return <p role="status">{t('settings.loading')}</p>;
  if (p.failed) return <p role="alert" className={fieldError}>{t('settings.loadFailed')}</p>;
  return (
    <Section title={t('clinicalAccess.requests.title')} intro={t('clinicalAccess.requests.intro')}>
      {p.requests.length === 0
        ? <p className="flex items-center gap-2 text-muted-foreground"><Inbox aria-hidden className="size-5" />{t('clinicalAccess.requests.empty')}</p>
        : (
          <ul className="flex flex-col divide-y rounded-lg border">
            {p.requests.map(r => {
              const who = r.requesterName ?? t('accessReview.unnamed');
              return (
                <li key={r.id} className="flex flex-wrap items-center justify-between gap-3 p-3">
                  <span className="flex min-w-0 flex-col">
                    <strong>{t('clinicalAccess.requests.who', { name: who, patient: r.patientName })}</strong>
                    <span className="text-sm text-muted-foreground">
                      {t('clinicalAccess.requests.mrn', { mrn: r.mrn })}{' · '}{t(`clinicalAccess.reason.${r.reason}`)}{' · '}{formatDate(new Date(r.requestedAt))}
                    </span>
                  </span>
                  <span className="flex flex-wrap items-center gap-2">
                    <Select value={String(p.daysFor(r.id))} onValueChange={v => p.onDays(r.id, Number(v))}>
                      <SelectTrigger className="h-11 w-36" aria-label={t('clinicalAccess.requests.durationFor', { name: who })}><SelectValue /></SelectTrigger>
                      <SelectContent>{p.grantDays.map(d => <SelectItem key={d} value={String(d)}>{tCount('clinicalAccess.requests.days', d)}</SelectItem>)}</SelectContent>
                    </Select>
                    <Button className="min-h-11" loading={p.decidingId === r.id} disabled={p.decidingId !== null}
                      aria-label={t('clinicalAccess.requests.grantFor', { name: who })} onClick={() => p.onGrant(r.id)}>{t('clinicalAccess.requests.grant')}</Button>
                    <Button variant="outline" className="min-h-11" disabled={p.decidingId !== null}
                      aria-label={t('clinicalAccess.requests.declineFor', { name: who })} onClick={() => p.onDecline(r.id)}>{t('clinicalAccess.requests.decline')}</Button>
                  </span>
                </li>
              );
            })}
          </ul>
        )}
      {p.decideError && <p role="alert" className={fieldError}>{t(`clinicalAccess.requests.error.${p.decideError}`)}</p>}
    </Section>
  );
}
