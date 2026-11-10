import { CircleCheck, CircleDashed, Info } from 'lucide-react';
import type { FormEventHandler } from 'react';
import type { UseFormReturn } from 'react-hook-form';
import { useI18n } from '@medvya/i18n';
import { Button, buttonVariants, Input, Label } from '@medvya/ui';
import { uploadErrorKey, type DocumentKind, type FileProblem, type ItContactForm, type OrganisationRequest, type RequestError } from '../domain/organisationRequest';

type Props = {
  request: OrganisationRequest | null;
  loading: boolean;
  forbidden: boolean;
  notFound: boolean;
  loadFailed: boolean;
  editable: boolean;
  missing: DocumentKind[];
  ready: boolean;
  uploading: DocumentKind | null;
  uploadError: RequestError | FileProblem | null;
  uploadErrorKind: DocumentKind | null;
  onFile: (kind: DocumentKind, file: File | undefined) => void;
  form: UseFormReturn<ItContactForm>;
  savingContact: boolean;
  contactSaved: boolean;
  contactFailed: boolean;
  onContact: FormEventHandler<HTMLFormElement>;
  submitting: boolean;
  submitFailed: boolean;
  onSubmit: () => void;
  startingNew: boolean;
  startNewFailed: boolean;
  onStartNew: () => void;
};

const error = 'text-sm font-semibold text-danger-700';

/** F-04 status page of the onboarding request (ADR-016): where it stands, Medvya's reason, the documents, and Submit. */
export default function OrganisationRequestPage(p: Props) {
  const { t } = useI18n();
  const { errors } = p.form.formState;
  if (p.loading) return <p role="status" className="p-6">{t('orgRequest.loading')}</p>;
  if (!p.request) return <p role="alert" className={`${error} p-6`}>{t(p.forbidden ? 'orgRequest.forbidden' : p.notFound ? 'orgRequest.none' : 'orgRequest.loadFailed')}</p>;
  const { request: r } = p;
  const uploadMessage = p.uploadError ? t(`orgRequest.error.${uploadErrorKey(p.uploadError)}`) : null;
  return (
    <section aria-labelledby="org-request-title" className="mx-auto flex w-full max-w-2xl flex-col gap-6 p-4 md:p-6">
      <header className="flex flex-col gap-2">
        <h1 id="org-request-title" className="text-2xl font-extrabold">{t('orgRequest.title')}</h1>
        <p className="text-muted-foreground">{t('orgRequest.intro', { type: t(`orgRequest.type.${r.type}`) })}</p>
        <p role="status" className="flex items-start gap-2 rounded-lg bg-muted p-3 font-semibold">
          <Info className="mt-0.5 size-4 shrink-0" aria-hidden />{t(`orgRequest.status.${r.state}`)}
        </p>
        {r.reason && (r.state === 'NeedsChanges' || r.state === 'Rejected' || r.state === 'Suspended') && (
          <p className="rounded-lg border p-3"><strong>{t('orgRequest.reason')}</strong> {r.reason}</p>
        )}
      </header>

      {r.state === 'Rejected' && (
        <div className="flex flex-col gap-2">
          <Button className="min-h-11 self-start" loading={p.startingNew} onClick={p.onStartNew}>{t('orgRequest.startNew')}</Button>
          {p.startNewFailed && <p role="alert" className={error}>{t('orgRequest.error.failed')}</p>}
        </div>
      )}

      <fieldset className="flex flex-col gap-3" disabled={!p.editable}>
        <legend className="mb-1 text-lg font-bold">{t('orgRequest.docs.legend')}</legend>
        <p className="text-sm text-muted-foreground">{t('orgRequest.docs.hint')}</p>
        <ul className="flex flex-col gap-3">
          {r.requiredDocuments.map(kind => {
            const added = r.documents.some(d => d.kind === kind);
            const id = `doc-${kind}`;
            const failed = p.uploadError && p.uploadErrorKind === kind;
            return (
              <li key={kind} className="flex flex-wrap items-center justify-between gap-3 rounded-xl border bg-card p-4">
                <div className="flex min-w-0 flex-col gap-1">
                  <Label htmlFor={id}>{t(`orgRequest.doc.${kind}`)}</Label>
                  <span className="flex items-center gap-1.5 text-sm">
                    {added ? <CircleCheck className="size-4" aria-hidden /> : <CircleDashed className="size-4" aria-hidden />}
                    {added ? t('orgRequest.doc.added') : t('orgRequest.doc.missing')}
                  </span>
                  {failed && uploadMessage && <span id={`${id}-error`} role="alert" className={error}>{uploadMessage}</span>}
                </div>
                {p.editable && (
                  <>
                    <input id={id} type="file" accept="application/pdf,image/png,image/jpeg" className="peer sr-only" disabled={p.uploading === kind}
                      aria-describedby={failed ? `${id}-error` : undefined} aria-invalid={failed ? true : undefined}
                      onChange={e => { p.onFile(kind, e.currentTarget.files?.[0]); e.currentTarget.value = ''; }} />
                    <label htmlFor={id} className={buttonVariants({ variant: 'outline', className: 'min-h-11 w-fit cursor-pointer peer-focus-visible:outline-2 peer-focus-visible:outline-offset-2 peer-focus-visible:outline-ring peer-disabled:pointer-events-none peer-disabled:opacity-50' })}>
                      {p.uploading === kind ? t('orgRequest.doc.uploading') : added ? t('orgRequest.doc.replace') : t('orgRequest.doc.choose')}
                    </label>
                  </>
                )}
              </li>
            );
          })}
        </ul>
      </fieldset>

      {r.type === 'Hospital' && (
        <form noValidate onSubmit={p.onContact} className="grid gap-4 sm:grid-cols-2">
          <fieldset className="contents" disabled={!p.editable}>
            <legend className="mb-1 text-lg font-bold sm:col-span-2">{t('orgRequest.it.legend')}</legend>
            <div className="flex flex-col gap-2">
              <Label htmlFor="it-name">{t('orgRequest.it.name')}</Label>
              <Input id="it-name" className="h-11" autoComplete="off" aria-invalid={errors.name ? true : undefined} aria-describedby={errors.name ? 'it-name-error' : undefined} {...p.form.register('name')} />
              {errors.name && <p id="it-name-error" role="alert" className={error}>{t(`onboarding.error.${errors.name.message === 'tooLong' ? 'tooLong' : 'required'}`)}</p>}
            </div>
            <div className="flex flex-col gap-2">
              <Label htmlFor="it-email">{t('orgRequest.it.email')}</Label>
              <Input id="it-email" type="email" dir="ltr" className="h-11" autoComplete="off" aria-invalid={errors.email ? true : undefined} aria-describedby={errors.email ? 'it-email-error' : undefined} {...p.form.register('email')} />
              {errors.email && <p id="it-email-error" role="alert" className={error}>{t(`onboarding.error.${errors.email.message === 'format' ? 'format' : 'required'}`)}</p>}
            </div>
            <div className="flex flex-wrap items-center gap-3 sm:col-span-2">
              <Button type="submit" variant="outline" className="min-h-11" loading={p.savingContact}>{t('orgRequest.it.save')}</Button>
              {p.contactSaved && <span role="status" className="flex items-center gap-1.5 text-sm font-semibold"><CircleCheck className="size-4" aria-hidden />{t('orgRequest.it.saved')}</span>}
              {p.contactFailed && <span role="alert" className={error}>{t('orgRequest.error.failed')}</span>}
            </div>
          </fieldset>
        </form>
      )}

      {p.editable ? (
        <div className="flex flex-col gap-2">
          <Button className="min-h-11 self-start" loading={p.submitting} disabled={!p.ready} onClick={p.onSubmit}>
            {t(r.state === 'NeedsChanges' ? 'orgRequest.resubmit' : 'orgRequest.submit')}
          </Button>
          {!p.ready && <p className="text-sm text-muted-foreground">{t('orgRequest.submit.missing')}</p>}
          {p.submitFailed && <p role="alert" className={error}>{t('orgRequest.error.submit')}</p>}
        </div>
      ) : null}
    </section>
  );
}
