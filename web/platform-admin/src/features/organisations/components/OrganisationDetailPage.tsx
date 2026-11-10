import type { FormEventHandler } from 'react';
import type { UseFormReturn } from 'react-hook-form';
import { ArrowLeft, FileText } from 'lucide-react';
import { Link } from 'react-router';
import { useI18n, type MessageKey } from '@medvya/i18n';
import { Button } from '@medvya/ui';
import ReasonPanel from '../../shell/components/ReasonPanel';
import { isDestructive, type Decision, type OrganisationDetail } from '../domain/organisations';
import type { ReasonForm } from '../../team/domain/team';

type Props = {
  vm: {
    state: 'loading' | 'error' | 'ready'; forbidden: boolean; notFound: boolean; detail: OrganisationDetail | null;
    decisions: Decision[]; pending: Decision | null; form: UseFormReturn<ReasonForm>; busy: boolean; done: Decision | null; problem: MessageKey | null;
    onStart: (d: Decision) => void; onCancel: () => void; onConfirm: FormEventHandler;
    documentFailed: boolean; onOpenDocument: (documentId: string) => void;
  };
};

/** One organisation's request: profile, documents, history and the decision (design: organisation approval detail; MDV-P1-064). */
export default function OrganisationDetailPage({ vm }: Props) {
  const { t, formatDate, formatTime } = useI18n();
  const when = (iso: string) => `${formatDate(new Date(iso))} ${formatTime(new Date(iso))}`;
  const back = (
    <Link to="/organisations" className="inline-flex min-h-11 items-center gap-1 self-start font-semibold text-primary underline-offset-4 hover:underline">
      <ArrowLeft aria-hidden className="size-4 rtl:rotate-180" />{t('platform.organisations.back')}
    </Link>
  );
  if (vm.state !== 'ready' || !vm.detail)
    return (
      <>
        {back}
        <p role={vm.state === 'loading' ? 'status' : 'alert'} className="font-semibold">
          {t(vm.state === 'loading' ? 'platform.state.loading' : vm.forbidden ? 'platform.state.forbidden' : vm.notFound ? 'platform.organisations.notFound' : 'platform.state.error')}
        </p>
      </>
    );
  const d = vm.detail;
  const profile: [MessageKey, string | null][] = [
    ['platform.organisations.field.type', t(`platform.organisations.type.${d.type}`)],
    ['platform.organisations.field.country', d.country],
    ['platform.organisations.field.submitted', d.submittedAt ? when(d.submittedAt) : null],
    ['platform.organisations.field.address', d.address],
    ['platform.organisations.field.contactEmail', d.contactEmail],
    ['platform.organisations.field.registration', d.registrationNumber],
    ['platform.organisations.field.tax', d.taxNumber],
    ...(d.type === 'Hospital' ? [['platform.organisations.field.itContact', [d.itContactName, d.itContactEmail].filter(Boolean).join(' · ') || null] as [MessageKey, string | null]] : []),
  ];
  return (
    <>
      {back}
      <div>
        <h1 dir="auto" className="text-2xl font-extrabold">{d.name}</h1>
        <p className="text-sm font-semibold">{t('platform.organisations.stateIs', { state: t(`platform.organisations.state.${d.state}`) })}</p>
        {d.reason && <p className="text-sm text-muted-foreground">{t('platform.organisations.lastReason')} <bdi>{d.reason}</bdi></p>}
      </div>
      {vm.done && <p role="status" className="font-semibold text-success-700">{t(`platform.organisations.done.${vm.done}`)}</p>}

      {vm.decisions.length > 0 && !vm.pending && (
        <div className="flex flex-wrap gap-2">
          {vm.decisions.map(x => <Button key={x} variant={isDestructive(x) ? 'outline' : 'default'} onClick={() => vm.onStart(x)}>{t(`platform.organisations.decision.${x}`)}</Button>)}
        </div>
      )}
      {vm.pending && (
        <ReasonPanel title={t(`platform.organisations.confirm.${vm.pending}.title`, { name: d.name })} form={vm.form}
          confirmLabel={t(`platform.organisations.decision.${vm.pending}`)} destructive={isDestructive(vm.pending)} busy={vm.busy}
          problem={vm.problem ? t(vm.problem) : null} onConfirm={vm.onConfirm} onCancel={vm.onCancel}>
          <p className="text-sm">{t(`platform.organisations.confirm.${vm.pending}.body`)}</p>
        </ReasonPanel>
      )}

      <section aria-labelledby="org-profile" className="flex flex-col gap-2 rounded-lg border border-border bg-card p-4">
        <h2 id="org-profile" className="text-base font-bold">{t('platform.organisations.profile')}</h2>
        <dl className="grid gap-x-6 gap-y-2 sm:grid-cols-2">
          {profile.map(([label, value]) => (
            <div key={label}>
              <dt className="text-sm text-muted-foreground">{t(label)}</dt>
              <dd dir={value ? 'auto' : undefined} className="font-semibold break-words">{value ?? t('platform.organisations.notGiven')}</dd>
            </div>
          ))}
        </dl>
      </section>

      <section aria-labelledby="org-documents" className="flex flex-col gap-2 rounded-lg border border-border bg-card p-4">
        <h2 id="org-documents" className="text-base font-bold">{t('platform.organisations.documents')}</h2>
        <p className="text-sm text-muted-foreground">{t('platform.organisations.documentsHint')}</p>
        {d.documents.length === 0 ? <p>{t('platform.organisations.noDocuments')}</p> : (
          <ul className="flex flex-col gap-1">
            {d.documents.map(doc => (
              <li key={doc.id}>
                <Button variant="ghost" onClick={() => vm.onOpenDocument(doc.id)}>
                  <FileText aria-hidden />{t(`platform.organisations.document.${doc.kind}` as MessageKey)}
                  <span className="text-sm font-normal text-muted-foreground">{formatDate(new Date(doc.uploadedAt))}</span>
                </Button>
              </li>
            ))}
          </ul>
        )}
        {vm.documentFailed && <p role="alert" className="text-sm font-semibold text-danger-700">{t('platform.organisations.documentFailed')}</p>}
      </section>

      <section aria-labelledby="org-history" className="flex flex-col gap-2 rounded-lg border border-border bg-card p-4">
        <h2 id="org-history" className="text-base font-bold">{t('platform.organisations.history')}</h2>
        {d.history.length === 0 ? <p>{t('platform.organisations.noHistory')}</p> : (
          <ol className="flex flex-col gap-2">
            {d.history.map(h => (
              <li key={`${h.at}-${h.state}`} className="text-sm">
                <span className="font-semibold">{t(`platform.organisations.state.${h.state}`)}</span>
                {' · '}{when(h.at)}{' · '}{h.actor ?? t('platform.organisations.operations')}
                {h.reason && <span dir="auto" className="block text-muted-foreground">{h.reason}</span>}
              </li>
            ))}
          </ol>
        )}
      </section>
    </>
  );
}
