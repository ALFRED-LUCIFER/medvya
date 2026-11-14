import type { ReactNode } from 'react';
import { Link } from 'react-router';
import { ArrowLeft, ClipboardList, Eye, TriangleAlert } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, buttonVariants, cn, PageContainer, PatientBanner } from '@medvya/ui';
import { addressLines, missingParts, type ContactView, type PatientDetails, type RevealedIdentifiers } from '../domain/directory';
import { consentPolicyVersion, genderIdentities, regionKey } from '../../registration/domain/registration';
import type { MergeHistoryEntry, Refusal } from '../../merge/domain/merge';
import { PatientPicker, type PatientSearchView } from '../../schedule/components/BookingPanel';
import { consentRows, stateOf, type PatientConsents } from '../domain/consents';
import PatientsMessage from './PatientsMessage';

type Props = {
  patient: PatientDetails | null;
  state: 'loading' | 'ready' | 'invalid' | 'forbidden' | 'notFound' | 'error';
  onRetry: () => void;
  backPath: string;
  /** Full numbers, after the audited reveal; null = masked to the last four. */
  revealed: RevealedIdentifiers | null;
  /** Only Register / book patients at Full may reveal (the server checks the same). */
  canReveal: boolean;
  revealing: boolean;
  revealFailed: boolean;
  onReveal: () => void;
  /** "Complete registration" for an incomplete record; null when this role may not register (Register / book at Full). */
  completePath: string | null;
  /** Book, Check in and Edit details: each null when this role may not (Register / book at Full; Check-in & queue at Full too for check-in). */
  actions: { bookPath: string | null; checkInPath: string | null; editPath: string | null };
  /** The patient's documents (MDV-P2-006); null when this role may not use them (the desk uploads and lists, Register / book and Check-in at Full). */
  documentsPath: string | null;
  /** Merges this record took part in (MDV-P1-017); empty for most records. */
  merges: MergeHistoryEntry[];
  /** Current consents and the change action (MDV-P1-019). Changing needs Register / book at Full, as the server checks. */
  consents: PatientConsents;
  /** The visit list (MDV-1088): date and status only, composed by the route. */
  visits: ReactNode;
  /** "Request merge review" with another record (MDV-P1-017); null when this role may not ask (Register / book at Full, as the server checks). */
  mergeRequest: { search: PatientSearchView; chosen: string; busy: boolean; refusal: Refusal | null; onRequest: () => void } | null;
};

// Keeps Latin IDs, phones and dates intact (order and line) inside Arabic text.
const Ltr = ({ children }: { children: ReactNode }) => <bdi dir="ltr">{children}</bdi>;

/** An opened record for the front desk: registration data only, never clinical data. Opening it was audited by the server. */
export default function PatientRecord(props: Props) {
  const { patient, state, onRetry, backPath } = props;
  const { t, formatCalendarDate, locale, country } = useI18n();
  const date = formatCalendarDate;
  const back = (
    <Link to={backPath} className={cn(buttonVariants({ variant: 'ghost' }), 'self-start px-2')}>
      <ArrowLeft className="rtl:rotate-180" aria-hidden />{t('patients.record.back')}
    </Link>
  );
  // One live region that stays mounted; only its text changes, so screen readers announce every change.
  const status = state === 'loading' ? t('patients.record.loading') : state === 'forbidden' ? t('err.403')
    : state === 'notFound' ? t('patients.record.notFound.title') : state === 'error' ? t('patients.error.title')
    : props.revealing ? t('patients.loading') : props.revealed ? t('patients.record.revealed') : '';
  const live = <p role="status" className="sr-only">{status}</p>;

  if (!patient)
    return (
      <PageContainer width="reading">
        {back}
        {live}
        {state === 'loading' ? <PatientsMessage pageHeading title={t('patients.record.loading')} />
          : state === 'forbidden' ? <PatientsMessage pageHeading title={t('err.403')} body={t('patients.forbidden.body')} />
          : state === 'notFound' ? <PatientsMessage pageHeading title={t('patients.record.notFound.title')} body={t('patients.record.notFound.body')} />
          : <PatientsMessage pageHeading title={t('patients.error.title')} body={t('patients.error.body')} action={{ label: t('patients.retry'), onClick: onRetry }} />}
      </PageContainer>
    );

  const p = patient;
  const none = <span className="text-muted-foreground">{t('patients.record.none')}</span>;
  const dob = p.dateOfBirthEstimated ? t('patients.record.dobEstimated', { date: date(p.dateOfBirth) }) : date(p.dateOfBirth);
  // Codes stored at registration are shown in the UI language; older free-text values are shown as entered.
  const gender = genderIdentities.find(g => g === p.genderIdentity);
  const named = (type: 'language' | 'region', code: string) => {
    try { return new Intl.DisplayNames([locale], { type }).of(code) ?? code; } catch { return code; } // free text from before codes: shown as entered
  };
  const region = p.address?.region ? regionKey(country, p.address.region) : null;
  const address = p.address && region ? { ...p.address, region: t(region) } : p.address;
  const masked = (last: string) => t('patients.masked', { last });
  const contact = (c: ContactView) => <><bdi>{c.name}</bdi> {t('registration.review.relationship', { relationship: t(`registration.relationship.${c.relationship}`) })} <Ltr>{c.phone}</Ltr></>;
  const full = (scheme: string) => props.revealed?.identifiers.find(i => i.scheme === scheme)?.value;
  const hasNumbers = p.identifiers.length > 0 || !!p.payer?.memberNumberLastFour;
  return (
    <PageContainer width="reading">
      {back}
      {live}
      {/* The desk variant (MDV-P1-092): identity only, whatever is passed; no allergy, diagnosis, medicine or vitals at the desk. */}
      <PatientBanner variant="desk" headingLevel={1} patient={{ name: p.fullName, age: p.ageYears, sex: p.sexAtBirth, mrn: p.mrn, dob: dob }} />
      <div className="flex flex-col gap-1">
        {p.arabicName && <p lang="ar" dir="rtl" className="self-start font-bold text-muted-foreground">{p.arabicName}</p>}
        {p.status === 'Incomplete' && (
          <p className="inline-flex items-center gap-1.5 self-start rounded-sm border px-2 py-1 text-sm font-bold text-warning-700">
            <TriangleAlert className="size-4" aria-hidden />{t('patients.status.Incomplete')}
          </p>
        )}
        <p className="text-xs text-muted-foreground">{t('patients.record.noClinical')}</p>
      </div>

      {(props.actions.bookPath || props.actions.checkInPath || props.actions.editPath) && (
        <div className="flex flex-wrap gap-2">
          {props.actions.bookPath && <Link to={props.actions.bookPath} className={buttonVariants()}>{t('patients.record.book')}</Link>}
          {props.actions.checkInPath && <Link to={props.actions.checkInPath} className={buttonVariants({ variant: 'outline' })}>{t('patients.record.checkIn')}</Link>}
          {props.actions.editPath && <Link to={props.actions.editPath} className={buttonVariants({ variant: 'outline' })}>{t('patients.record.edit')}</Link>}
        </div>
      )}

      {props.documentsPath && <Link to={props.documentsPath} className={buttonVariants({ variant: 'outline', className: 'self-start' })}>{t('documents.title')}</Link>}

      {p.status === 'Incomplete' && props.completePath && (
        // The desk's completion task (MDV-P1-015): what quick registration left out, closed once address and payer exist.
        <section aria-labelledby="completion-task" className="flex flex-col items-start gap-2 rounded-xl border bg-card p-4 shadow-[inset_4px_0_0_var(--color-warning-700)] rtl:shadow-[inset_-4px_0_0_var(--color-warning-700)]">
          <h2 id="completion-task" className="flex items-center gap-2 font-extrabold"><ClipboardList className="size-5 text-warning-700" aria-hidden />{t('patients.completion.title')}</h2>
          <p>{t('patients.completion.missing', { items: new Intl.ListFormat([locale], { type: 'conjunction' }).format(missingParts(p).map(m => t(`patients.completion.item.${m}`))) })}</p>
          <Link to={props.completePath} className={buttonVariants()}>{t('patients.completion.action')}</Link>
        </section>
      )}

      <Section title={t('patients.record.identity')}>
        <Detail label={t('patients.record.dob')}>{dob}</Detail>
        <Detail label={t('patients.record.sex')}>{t(`patients.sex.${p.sexAtBirth}`)}</Detail>
        <Detail label={t('patients.record.gender')}>{gender ? t(`registration.gender.${gender}`) : p.genderIdentity ?? none}</Detail>
        <Detail label={t('patients.record.language')}>{p.preferredLanguage ? named('language', p.preferredLanguage) : none}</Detail>
        <Detail label={t('patients.record.nationality')}>{p.nationality ? named('region', p.nationality) : none}</Detail>
        {/* A calendar date in the facility's country time zone, from the server; never re-derived from a UTC instant. */}
        <Detail label={t('patients.record.registered')}>{date(p.registeredOn)}</Detail>
      </Section>

      <Section title={t('patients.record.identifiers')}>
        {p.identifiers.length === 0 ? <Detail label={t('patients.record.identifiers')}>{none}</Detail>
          : p.identifiers.map(i => (
            <Detail key={i.scheme} label={t(`patients.scheme.${i.scheme}`)}>
              <Ltr>{full(i.scheme) ?? masked(i.lastFour)}</Ltr>
              {i.expiresOn && <span className="ms-1 text-muted-foreground">{t('patients.record.expires', { date: date(i.expiresOn) })}</span>}
            </Detail>
          ))}
      </Section>

      <Section title={t('patients.record.contact')}>
        <Detail label={t('patients.record.mobile')}><Ltr>{p.mobile}</Ltr></Detail>
        <Detail label={t('patients.record.alternate')}>{p.alternatePhone ? <Ltr>{p.alternatePhone}</Ltr> : none}</Detail>
        <Detail label={t('patients.record.email')}>{p.email ? <Ltr>{p.email}</Ltr> : none}</Detail>
        <Detail label={t('patients.record.address')}>{address ? <bdi>{addressLines(address).join(', ')}</bdi> : none}</Detail>
      </Section>

      <Section title={t('patients.record.contacts')}>
        <Detail label={t('patients.record.emergencyContact')}>{p.emergencyContact ? contact(p.emergencyContact) : none}</Detail>
        <Detail label={t('patients.record.guardian')}>{p.guardian ? contact(p.guardian) : none}</Detail>
      </Section>

      <Section title={t('patients.record.payer')}>
        {p.payer ? <>
          <Detail label={t('patients.record.payerKind')}>{t(`patients.payer.${p.payer.kind}`)}</Detail>
          {p.payer.payerName && <Detail label={t('patients.record.payerName')}><bdi>{p.payer.payerName}</bdi></Detail>}
          {p.payer.plan && <Detail label={t('patients.record.plan')}><bdi>{p.payer.plan}</bdi></Detail>}
          {p.payer.memberNumberLastFour && (
            <Detail label={t('patients.record.memberNumber')}><Ltr>{props.revealed?.memberNumber ?? masked(p.payer.memberNumberLastFour)}</Ltr></Detail>
          )}
          {p.payer.validTo && <Detail label={t('patients.record.validTo')}>{date(p.payer.validTo)}</Detail>}
        </> : <Detail label={t('patients.record.payerKind')}>{t('patients.record.payerMissing')}</Detail>}
      </Section>

      <section aria-labelledby="consents-title" className="rounded-xl border bg-card p-4">
        <h2 id="consents-title" className="text-base font-extrabold">{t('patients.consents.title')}</h2>
        <p className="mb-3 text-xs text-muted-foreground">{t('patients.consents.note')} <bdi dir="ltr">{t('registration.consents.version', { version: consentPolicyVersion })}</bdi></p>
        {props.consents.failed ? <p role="alert" className="text-sm font-semibold text-danger-700">{t('patients.consents.failed')}</p> : (
          <ul className="flex flex-col divide-y">
            <li className="flex min-h-11 items-center justify-between gap-3 py-1">
              <span className="font-bold">{t('registration.consents.treatment.title')}</span>
              <span className="text-sm font-bold">{t('registration.consents.required')}</span>
            </li>
            {consentRows.map(row => {
              const state = stateOf(props.consents.states, row);
              const label = row.type === 'Reminders' ? `${t('registration.consents.reminders.title')}: ${t(row.title)}` : t(row.title);
              const action = t(state?.granted ? 'patients.consents.withdraw' : 'patients.consents.grant');
              return (
                <li key={row.id} className="flex min-h-11 items-center justify-between gap-3 py-1">
                  <span className="min-w-0 font-bold">{label}</span>
                  <span className="flex items-center gap-3">
                    <span className="text-sm font-bold">{state ? t(state.granted ? 'registration.consents.on' : 'registration.consents.off') : t('patients.consents.notRecorded')}</span>
                    {props.canReveal && (
                      <Button variant="outline" disabled={props.consents.loading} loading={props.consents.changing === row.id}
                        aria-label={`${action}: ${label}`} onClick={() => props.consents.onChange(row, !state?.granted)}>
                        {action}
                      </Button>
                    )}
                  </span>
                </li>
              );
            })}
          </ul>
        )}
        {props.consents.changeFailed && <p role="alert" className="mt-2 text-sm font-semibold text-danger-700">{t('patients.consents.changeFailed')}</p>}
      </section>

      {props.visits}

      {props.merges.length > 0 && (
        <section aria-labelledby="merge-history" className="rounded-xl border bg-card p-4">
          <h2 id="merge-history" className="mb-3 text-base font-extrabold">{t('merge.history')}</h2>
          <ul className="flex flex-col gap-1">
            {props.merges.map(m => (
              <li key={m.id}>
                {t(m.status === 'Merged' ? (m.thisIsSurvivor ? 'merge.history.survivor' : 'merge.history.merged') : 'merge.history.undone',
                  { mrn: m.otherMrn, date: date(m.status === 'Unmerged' && m.unmergedAt ? m.unmergedAt : m.mergedAt ?? '') })}
              </li>
            ))}
          </ul>
        </section>
      )}

      {props.mergeRequest && (
        <section aria-labelledby="merge-request" className="flex flex-col gap-3 rounded-xl border bg-card p-4">
          <h2 id="merge-request" className="text-base font-extrabold">{t('merge.request.title')}</h2>
          <p className="text-sm text-muted-foreground">{t('merge.request.body')}</p>
          <PatientPicker search={props.mergeRequest.search} chosen={props.mergeRequest.chosen} registerPath={null} invalid={false} />
          <div className="flex flex-col items-start gap-1">
            <Button variant="outline" loading={props.mergeRequest.busy} disabled={!props.mergeRequest.chosen} onClick={props.mergeRequest.onRequest}>{t('merge.request.action')}</Button>
            {props.mergeRequest.refusal && <p role="alert" className="text-sm font-semibold text-danger-700">{t(`merge.refused.${props.mergeRequest.refusal}`)}</p>}
          </div>
        </section>
      )}

      {props.canReveal && hasNumbers && !props.revealed && (
        <div className="flex flex-col items-start gap-1">
          <Button variant="outline" loading={props.revealing} onClick={props.onReveal}><Eye aria-hidden />{t('patients.record.reveal')}</Button>
          <p className="text-xs text-muted-foreground">{t('patients.record.revealNote')}</p>
          {props.revealFailed && <p className="text-sm font-semibold text-danger-700">{t('patients.record.revealFailed')}</p>}
        </div>
      )}
    </PageContainer>
  );
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="rounded-xl border bg-card p-4">
      <h2 className="mb-3 text-base font-extrabold">{title}</h2>
      <dl className="grid gap-x-6 gap-y-3 sm:grid-cols-2">{children}</dl>
    </section>
  );
}

function Detail({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex min-w-0 flex-col">
      <dt className="text-xs font-bold text-muted-foreground">{label}</dt>
      <dd className="break-words">{children}</dd>
    </div>
  );
}
