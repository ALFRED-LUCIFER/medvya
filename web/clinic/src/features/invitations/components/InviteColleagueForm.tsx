import type { FormEventHandler } from 'react';
import { Controller, type UseFormReturn } from 'react-hook-form';
import { useI18n } from '@medvya/i18n';
import { Button, Input, Label, Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@medvya/ui';
import { invitableRoles, type Facility, type InvitationCreated, type InviteInput } from '../domain/invitation';

type Props = {
  form: UseFormReturn<InviteInput>;
  loading: boolean;
  forbidden: boolean;
  loadFailed: boolean;
  facilities: Facility[];
  sending: boolean;
  sent: InvitationCreated | null;
  sendFailed: boolean;
  onSubmit: FormEventHandler<HTMLFormElement>;
};

const fieldError = 'text-sm font-semibold text-danger-700';

/** Minimal "Invite colleague" form: email, role, facility. Mobile/SMS invites arrive with MDV-P1-033. */
export default function InviteColleagueForm({ form, loading, forbidden, loadFailed, facilities, sending, sent, sendFailed, onSubmit }: Props) {
  const { t, formatDate } = useI18n();
  const { errors } = form.formState;
  let body;
  if (loading) body = <p aria-busy="true">{t('inviteForm.loading')}</p>;
  else if (loadFailed) body = <p role="alert" className={fieldError}>{t(forbidden ? 'inviteForm.forbidden' : 'inviteForm.loadFailed')}</p>;
  else
    body = (
      <form noValidate onSubmit={onSubmit} className="flex flex-col gap-4">
        <div className="flex flex-col gap-2">
          <Label htmlFor="invite-email">{t('inviteForm.email')}</Label>
          <Input id="invite-email" type="email" autoComplete="off" className="h-11" aria-invalid={!!errors.email}
            aria-describedby={errors.email ? 'invite-email-error' : undefined} {...form.register('email')} />
          {errors.email && <p id="invite-email-error" className={fieldError}>{t('inviteForm.error.email')}</p>}
        </div>
        <div className="flex flex-col gap-2">
          <Label htmlFor="invite-role">{t('inviteForm.role')}</Label>
          <Controller control={form.control} name="role" render={({ field }) => (
            <Select value={field.value} onValueChange={field.onChange}>
              <SelectTrigger id="invite-role" className="h-11 w-full"><SelectValue /></SelectTrigger>
              <SelectContent>{invitableRoles.map(r => <SelectItem key={r} value={r}>{t(`role.${r}`)}</SelectItem>)}</SelectContent>
            </Select>
          )} />
        </div>
        <div className="flex flex-col gap-2">
          <Label htmlFor="invite-facility">{t('inviteForm.facility')}</Label>
          <Controller control={form.control} name="facilityId" render={({ field }) => (
            <Select value={field.value} onValueChange={field.onChange}>
              <SelectTrigger id="invite-facility" className="h-11 w-full" aria-invalid={!!errors.facilityId}
                aria-describedby={errors.facilityId ? 'invite-facility-error' : undefined}>
                <SelectValue placeholder={t('inviteForm.facilityPlaceholder')} />
              </SelectTrigger>
              <SelectContent>{facilities.map(f => <SelectItem key={f.id} value={f.id}>{f.name}</SelectItem>)}</SelectContent>
            </Select>
          )} />
          {errors.facilityId && <p id="invite-facility-error" className={fieldError}>{t('inviteForm.error.facility')}</p>}
        </div>
        <Button type="submit" loading={sending}>{t('inviteForm.submit')}</Button>
        <div aria-live="polite" className="flex flex-col gap-1 text-sm">
          {sent && <p className="font-semibold text-success-700">{t('inviteForm.sent', { date: formatDate(new Date(sent.expiresAt)) })}</p>}
          {sent?.licenceVerificationRequired && <p>{t('inviteForm.licence')}</p>}
          {sendFailed && <p role="alert" className={fieldError}>{t('inviteForm.failed')}</p>}
        </div>
      </form>
    );
  // Renders inside the app shell, which owns the page landmark and the language switcher.
  return (
    <div className="flex max-w-[560px] flex-col gap-4 rounded-xl border bg-card p-6">
      <h1 className="text-xl font-extrabold">{t('inviteForm.title')}</h1>
      <p className="text-muted-foreground">{t('inviteForm.intro')}</p>
      {body}
    </div>
  );
}
