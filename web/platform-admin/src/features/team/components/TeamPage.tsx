import { useId } from 'react';
import { Copy, FileCheck, KeyRound, UserPlus, UserX } from 'lucide-react';
import { useI18n, type MessageKey } from '@medvya/i18n';
import { Button, Input, Label, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Table, TableBody, TableCell, TableHead, TableHeader, TableRow, TableState } from '@medvya/ui';
import type { FormEventHandler } from 'react';
import { Controller, type UseFormReturn } from 'react-hook-form';
import ReasonPanel from '../../shell/components/ReasonPanel';
import { keyState, roles, type InviteForm, type PendingAction, type ReasonForm, type ShownLink, type Team } from '../domain/team';

type Props = {
  vm: {
    state: 'loading' | 'error' | 'ready'; forbidden: boolean; team: Team | null; reviewDue: boolean;
    inviting: boolean; inviteForm: UseFormReturn<InviteForm>; inviteBusy: boolean; inviteFailed: boolean;
    onStartInvite: () => void; onCancelInvite: () => void; onInvite: FormEventHandler;
    pending: PendingAction | null; reasonForm: UseFormReturn<ReasonForm>; actionBusy: boolean; actionRefused: boolean; actionFailed: boolean;
    onStartAction: (action: PendingAction) => void; onCancelAction: () => void; onConfirmAction: FormEventHandler;
    issued: ShownLink | null; copied: boolean; onCopyLink: () => void; onDismissLink: () => void;
  };
};

/** Medvya team access (design: Medvya Admin, team access): who may use the console. Least access by default; reviewed every quarter. */
export default function TeamPage({ vm }: Props) {
  const { t, formatDate, formatTime, formatCalendarDate } = useI18n();
  const ids = { name: useId(), email: useId(), role: useId(), ends: useId() };
  const errorText = (code?: string) => (code ? t(`platform.error.${code}` as MessageKey) : null);
  const when = (iso: string) => `${formatDate(new Date(iso))} ${formatTime(new Date(iso))}`;
  const e = vm.inviteForm.formState.errors;

  return (
    <>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-2xl font-extrabold">{t('platform.team.title')}</h1>
          <p className="text-sm text-muted-foreground">{t('platform.team.lead')}</p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button variant="outline" onClick={() => vm.onStartAction({ kind: 'review' })}><FileCheck aria-hidden />{t('platform.team.startReview')}</Button>
          <Button onClick={vm.onStartInvite}><UserPlus aria-hidden />{t('platform.team.invite')}</Button>
        </div>
      </div>

      {vm.team && (
        <p role="status" className={vm.reviewDue ? 'font-semibold text-danger-700' : 'text-sm text-muted-foreground'}>
          {vm.reviewDue ? t('platform.team.reviewDueNow') : t('platform.team.reviewDue', { date: formatDate(new Date(vm.team.accessReviewDueAt)) })}
          {vm.team.lastAccessReviewAt && ` · ${t('platform.team.lastReview', { date: formatDate(new Date(vm.team.lastAccessReviewAt)) })}`}
        </p>
      )}

      {vm.issued && (
        <section aria-labelledby="issued-link" className="flex flex-col gap-2 rounded-lg border border-border bg-card p-4">
          <h2 id="issued-link" className="text-base font-bold">{t(vm.issued.purpose === 'invite' ? 'platform.link.inviteTitle' : 'platform.link.recoverTitle')}</h2>
          <p className="text-sm">{t('platform.link.body', { time: when(vm.issued.expiresAt) })}</p>
          <p dir="ltr" className="rounded-md bg-accent p-2 font-mono text-sm break-all">{vm.issued.url}</p>
          <div className="flex flex-wrap gap-2">
            <Button variant="outline" onClick={vm.onCopyLink}><Copy aria-hidden />{t(vm.copied ? 'platform.link.copied' : 'platform.link.copy')}</Button>
            <Button variant="ghost" onClick={vm.onDismissLink}>{t('platform.link.done')}</Button>
          </div>
        </section>
      )}

      {vm.inviting && (
        <section aria-labelledby="invite-title" className="flex flex-col gap-3 rounded-lg border border-border bg-card p-4">
          <h2 id="invite-title" className="text-base font-bold">{t('platform.team.inviteTitle')}</h2>
          <form onSubmit={vm.onInvite} noValidate className="grid gap-3 sm:grid-cols-2">
            <div className="flex flex-col gap-1.5">
              <Label htmlFor={ids.name}>{t('platform.team.name')}</Label>
              <Input id={ids.name} autoComplete="off" aria-invalid={e.displayName ? true : undefined} {...vm.inviteForm.register('displayName')} />
              {e.displayName && <p className="text-sm font-semibold text-danger-700">{errorText(e.displayName.message)}</p>}
            </div>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor={ids.email}>{t('platform.team.email')}</Label>
              <Input id={ids.email} type="email" dir="ltr" autoComplete="off" aria-invalid={e.email ? true : undefined} {...vm.inviteForm.register('email')} />
              {e.email && <p className="text-sm font-semibold text-danger-700">{errorText(e.email.message)}</p>}
            </div>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor={ids.role}>{t('platform.team.role')}</Label>
              <Controller control={vm.inviteForm.control} name="role" render={({ field }) => (
                <Select value={field.value} onValueChange={field.onChange}>
                  <SelectTrigger id={ids.role} className="min-h-11"><SelectValue /></SelectTrigger>
                  <SelectContent>{roles.map(r => <SelectItem key={r} value={r}>{t(`platform.role.${r}`)}</SelectItem>)}</SelectContent>
                </Select>
              )} />
            </div>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor={ids.ends}>{t('platform.team.accessEnds')}</Label>
              <Input id={ids.ends} type="date" aria-describedby={`${ids.ends}-hint`} {...vm.inviteForm.register('accessEndsOn')} />
              <p id={`${ids.ends}-hint`} className="text-sm text-muted-foreground">{t('platform.team.accessEndsHint')}</p>
            </div>
            <div className="flex flex-col gap-1.5 sm:col-span-2">
              <Label htmlFor={`${ids.name}-reason`}>{t('platform.reason.label')}</Label>
              <Input id={`${ids.name}-reason`} aria-invalid={e.reason ? true : undefined} {...vm.inviteForm.register('reason')} />
              <p className={e.reason ? 'text-sm font-semibold text-danger-700' : 'text-sm text-muted-foreground'}>{e.reason ? errorText(e.reason.message) : t('platform.reason.hint')}</p>
            </div>
            {vm.inviteFailed && <p role="alert" className="text-sm font-semibold text-danger-700 sm:col-span-2">{t('platform.error.failed')}</p>}
            <div className="flex flex-wrap gap-2 sm:col-span-2">
              <Button type="submit" loading={vm.inviteBusy}>{t('platform.team.sendInvite')}</Button>
              <Button type="button" variant="outline" onClick={vm.onCancelInvite}>{t('platform.action.cancel')}</Button>
            </div>
          </form>
        </section>
      )}

      {vm.pending && (
        <ReasonPanel
          title={vm.pending.kind === 'review' ? t('platform.team.reviewTitle') : t(vm.pending.kind === 'recover' ? 'platform.team.recoverTitle' : 'platform.team.removeTitle', { name: vm.pending.name })}
          form={vm.reasonForm}
          confirmLabel={t(vm.pending.kind === 'review' ? 'platform.team.reviewConfirm' : vm.pending.kind === 'recover' ? 'platform.team.recoverConfirm' : 'platform.team.removeConfirm')}
          destructive={vm.pending.kind !== 'review'}
          busy={vm.actionBusy}
          problem={vm.actionRefused ? t('platform.team.refused') : vm.actionFailed ? t('platform.error.failed') : null}
          onConfirm={vm.onConfirmAction}
          onCancel={vm.onCancelAction}>
          <p className="text-sm">{t(vm.pending.kind === 'review' ? 'platform.team.reviewBody' : vm.pending.kind === 'recover' ? 'platform.team.recoverBody' : 'platform.team.removeBody')}</p>
        </ReasonPanel>
      )}

      <TableState state={vm.state} messages={{ loading: t('platform.state.loading'), error: t(vm.forbidden ? 'platform.state.forbidden' : 'platform.state.error'), empty: '' }}>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>{t('platform.team.person')}</TableHead>
              <TableHead>{t('platform.team.role')}</TableHead>
              <TableHead>{t('platform.team.key')}</TableHead>
              <TableHead>{t('platform.team.lastActive')}</TableHead>
              <TableHead>{t('platform.team.accessEnds')}</TableHead>
              <TableHead><span className="sr-only">{t('platform.team.actions')}</span></TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {vm.team?.members.map(m => (
              <TableRow key={m.id}>
                <TableCell><p className="font-semibold">{m.displayName}</p><p dir="ltr" className="text-sm text-muted-foreground">{m.email}</p></TableCell>
                <TableCell>{t(`platform.role.${m.role}`)}</TableCell>
                <TableCell>{t(`platform.team.keyState.${keyState(m)}`)}</TableCell>
                <TableCell>{m.lastActiveAt ? when(m.lastActiveAt) : t('platform.team.never')}</TableCell>
                <TableCell>{m.accessEndsOn ? formatCalendarDate(m.accessEndsOn) : t('platform.team.untilRemoved')}</TableCell>
                <TableCell>
                  <div className="flex flex-wrap gap-2">
                    <Button variant="outline" aria-label={t('platform.team.recoverFor', { name: m.displayName })}
                      onClick={() => vm.onStartAction({ kind: 'recover', memberId: m.id, name: m.displayName })}><KeyRound aria-hidden />{t('platform.team.recover')}</Button>
                    <Button variant="ghost" aria-label={t('platform.team.removeFor', { name: m.displayName })}
                      onClick={() => vm.onStartAction({ kind: 'remove', memberId: m.id, name: m.displayName })}><UserX aria-hidden />{t('platform.team.remove')}</Button>
                  </div>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableState>
    </>
  );
}
