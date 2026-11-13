import { useId } from 'react';
import { Copy, FileCheck, KeyRound, UserPlus, UserX } from 'lucide-react';
import { useI18n, type MessageKey } from '@medvya/i18n';
import { Button, Card, ErrorSummary, Field, FieldMarks, Input, PageContainer, PageHeader, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Table, TableBody, TableCell, TableHead, TableHeader, TableRow, TableState } from '@medvya/ui';
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
  const errorText = (code?: string) => (code ? t(`platform.error.${code}` as MessageKey) : undefined);
  const when = (iso: string) => `${formatDate(new Date(iso))} ${formatTime(new Date(iso))}`;
  const e = vm.inviteForm.formState.errors;
  const inviteErrors = ([['displayName', ids.name, 'platform.team.name'], ['email', ids.email, 'platform.team.email'], ['accessEndsOn', ids.ends, 'platform.team.accessEnds'],
    ['reason', `${ids.name}-reason`, 'platform.reason.label']] as const)
    .flatMap(([name, id, label]) => (e[name]?.message ? [{ id, label: t(label), message: errorText(e[name]?.message)! }] : []));

  return (
    <PageContainer>
      <PageHeader title={t('platform.team.title')} description={t('platform.team.lead')} actions={<>
        <Button variant="outline" onClick={() => vm.onStartAction({ kind: 'review' })}><FileCheck aria-hidden />{t('platform.team.startReview')}</Button>
        <Button onClick={vm.onStartInvite}><UserPlus aria-hidden />{t('platform.team.invite')}</Button>
      </>} />

      {vm.team && (
        <p role="status" className={vm.reviewDue ? 'font-semibold text-danger-700' : 'text-sm text-muted-foreground'}>
          {vm.reviewDue ? t('platform.team.reviewDueNow') : t('platform.team.reviewDue', { date: formatDate(new Date(vm.team.accessReviewDueAt)) })}
          {vm.team.lastAccessReviewAt && ` · ${t('platform.team.lastReview', { date: formatDate(new Date(vm.team.lastAccessReviewAt)) })}`}
        </p>
      )}

      {vm.issued && (
        <Card title={t(vm.issued.purpose === 'invite' ? 'platform.link.inviteTitle' : 'platform.link.recoverTitle')}>
          <p className="text-sm">{t('platform.link.body', { time: when(vm.issued.expiresAt) })}</p>
          <p dir="ltr" className="rounded-md bg-accent p-2 font-mono text-sm break-all">{vm.issued.url}</p>
          <div className="flex flex-wrap gap-2">
            <Button variant="outline" onClick={vm.onCopyLink}><Copy aria-hidden />{t(vm.copied ? 'platform.link.copied' : 'platform.link.copy')}</Button>
            <Button variant="ghost" onClick={vm.onDismissLink}>{t('platform.link.done')}</Button>
          </div>
        </Card>
      )}

      {vm.inviting && (
        <Card title={t('platform.team.inviteTitle')}>
          <form onSubmit={vm.onInvite} noValidate className="grid gap-3 sm:grid-cols-2">
            <ErrorSummary title={t('field.errorSummary')} focusKey={vm.inviteForm.formState.submitCount} errors={inviteErrors} className="sm:col-span-2" />
            {/* Most answers are required, so only the optional end date is marked, in words. */}
            <FieldMarks mark="optional">
              <Field id={ids.name} label={t('platform.team.name')} required error={errorText(e.displayName?.message)}>
                <Input autoComplete="off" {...vm.inviteForm.register('displayName')} />
              </Field>
              <Field id={ids.email} label={t('platform.team.email')} required error={errorText(e.email?.message)}>
                <Input type="email" dir="ltr" autoComplete="off" {...vm.inviteForm.register('email')} />
              </Field>
              <Controller control={vm.inviteForm.control} name="role" render={({ field }) => (
                <Select value={field.value} onValueChange={field.onChange}>
                  <Field id={ids.role} label={t('platform.team.role')} required>
                    <SelectTrigger><SelectValue /></SelectTrigger>
                  </Field>
                  <SelectContent>{roles.map(r => <SelectItem key={r} value={r}>{t(`platform.role.${r}`)}</SelectItem>)}</SelectContent>
                </Select>
              )} />
              <Field id={ids.ends} label={t('platform.team.accessEnds')} hint={t('platform.team.accessEndsHint')} error={errorText(e.accessEndsOn?.message)}>
                <Input type="date" {...vm.inviteForm.register('accessEndsOn')} />
              </Field>
              <Field id={`${ids.name}-reason`} label={t('platform.reason.label')} required hint={t('platform.reason.hint')} error={errorText(e.reason?.message)} className="sm:col-span-2">
                <Input {...vm.inviteForm.register('reason')} />
              </Field>
            </FieldMarks>
            {vm.inviteFailed && <p role="alert" className="text-sm font-semibold text-danger-700 sm:col-span-2">{t('platform.error.failed')}</p>}
            <div className="flex flex-wrap gap-2 sm:col-span-2">
              <Button type="submit" loading={vm.inviteBusy}>{t('platform.team.sendInvite')}</Button>
              <Button type="button" variant="outline" onClick={vm.onCancelInvite}>{t('platform.action.cancel')}</Button>
            </div>
          </form>
        </Card>
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
        <Table label={t('platform.team.title')}>
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
    </PageContainer>
  );
}
