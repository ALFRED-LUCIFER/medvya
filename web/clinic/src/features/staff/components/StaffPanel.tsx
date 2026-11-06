import { Lock, Plus } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, Input, Table, TableBody, TableCell, TableHead, TableHeader, TableRow, TableState, Textarea, type SortDirection } from '@medvya/ui';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@medvya/ui';
import type { FormEventHandler } from 'react';
import type { UseFormReturn } from 'react-hook-form';
import { describedBy, fieldError } from '../../settings/components/fieldStyles';
import type { Location } from '../../settings/domain/settings';
import { Field } from '../../settings/components/Field';
import { Section } from '../../settings/components/Section';
import { staffColumns, type StaffColumn } from '../domain/staffTable';
import { assignableRoles, canOffboard, canResend, canRevoke, permissionRows, type AddStaffForm, type OffboardForm, type RoleCatalogue, type RoleChoice, type StaffMember, type Step } from '../domain/staff';

type Props = {
  rows: StaffMember[];
  sortOf: (key: StaffColumn) => SortDirection;
  onSort: (key: StaffColumn) => void;
  loading: boolean;
  failed: boolean;
  catalogue: RoleCatalogue;
  locations: Location[];
  step: Step | null;
  choice: RoleChoice | null;
  form: UseFormReturn<AddStaffForm>;
  canContinue: boolean;
  notice: 'invited' | 'resent' | 'revoked' | 'offboarded' | null;
  offboarding: StaffMember | null;
  offboardForm: UseFormReturn<OffboardForm>;
  offboardWorking: boolean;
  offboardError: 'lastAdmin' | 'failed' | null;
  onOffboard: (member: StaffMember) => void;
  onOffboardSubmit: FormEventHandler<HTMLFormElement>;
  onOffboardCancel: () => void;
  adding: boolean;
  addFailed: boolean;
  emailFailed: boolean;
  working: boolean;
  rowFailed: boolean;
  onOpen: () => void;
  onChoice: (choice: RoleChoice) => void;
  onNext: () => void;
  onBack: () => void;
  onClose: () => void;
  onSubmit: FormEventHandler<HTMLFormElement>;
  onResend: (invitationId: string) => void;
  onRevoke: (invitationId: string) => void;
};

const stepOrder = ['role', 'scope', 'permissions'] as const;
const same = (a: RoleChoice | null, b: RoleChoice) => !!a && a.kind === b.kind && ('role' in b ? 'role' in a && a.role === b.role : 'id' in b ? 'id' in a && a.id === b.id : true);

/** Staff (F-10): who works here and with what access, and the steps to add someone. Doctors are added under Doctors, not here. */
export default function StaffPanel(p: Props) {
  const { t, formatDate } = useI18n();
  const { errors } = p.form.formState;
  const tableState = p.loading ? 'loading' : p.failed ? 'error' : p.rows.length === 0 ? 'empty' : 'ready';

  const roleName = (m: StaffMember) => m.customRoleName ?? t(`role.${m.role}`);
  const choices: { choice: RoleChoice; name: string; description: string }[] = [
    ...assignableRoles.map(role => ({ choice: { kind: 'builtIn', role } as RoleChoice, name: t(`role.${role}`), description: t(`staff.roleDesc.${role}`) })),
    ...p.catalogue.custom.map(c => ({ choice: { kind: 'custom', id: c.id } as RoleChoice, name: c.name, description: t('staff.roleDesc.custom', { base: t(`role.${c.baseRole}`) }) })),
    { choice: { kind: 'doctor' }, name: t('role.HospitalDoctor'), description: t('staff.roleDesc.Doctor') },
  ];
  const stepIndex = p.step ? stepOrder.indexOf(p.step) : -1;

  return (
    <div className="flex flex-col gap-4">
      <Section title={t('staff.title')} intro={t('staff.intro')}>
        <div aria-live="polite" className="text-sm font-semibold text-success-700">{p.notice && t(`staff.notice.${p.notice}`)}</div>
        {p.rowFailed && <p role="alert" className={fieldError}>{t('staff.rowFailed')}</p>}
        {p.emailFailed && !p.adding && <p role="alert" className={fieldError}>{t('staff.error.email')}</p>}
        <TableState state={tableState} messages={{ loading: t('settings.loading'), error: t('settings.loadFailed'), empty: t('staff.empty') }}>
          <Table label={t('staff.tableLabel')} className="min-w-[720px]">
            <TableHeader>
              <TableRow>
                {staffColumns.map(c => (
                  <TableHead key={c.key} scope="col" sort={c.sortable ? p.sortOf(c.key) : undefined} onSort={c.sortable ? () => p.onSort(c.key) : undefined}>{t(`staff.col.${c.key}`)}</TableHead>
                ))}
                <TableHead scope="col"><span className="sr-only">{t('staff.col.actions')}</span></TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {p.rows.map(m => {
                const who = m.name ?? m.email ?? '';
                return (
                  <TableRow key={m.membershipId}>
                    <TableHead scope="row">{who}</TableHead>
                    <TableCell>{roleName(m)}</TableCell>
                    <TableCell>{m.facilityName}</TableCell>
                    <TableCell>{t(`staff.mfa.${m.mfa}`)}</TableCell>
                    <TableCell>
                      {t(`staff.status.${m.status}`)}
                      {m.invitationState && <span className="block text-muted-foreground">{t(`staff.invitation.${m.invitationState}`, { date: m.invitationExpiresAt ? formatDate(new Date(m.invitationExpiresAt)) : '' })}</span>}
                    </TableCell>
                    <TableCell>
                      <span className="flex flex-wrap gap-2">
                        {canResend(m) && <Button type="button" variant="outline" className="min-h-11 min-w-11" disabled={p.working} aria-label={t('staff.resendNamed', { name: who })} onClick={() => p.onResend(m.invitationId!)}>{t('staff.resend')}</Button>}
                        {canRevoke(m) && <Button type="button" variant="outline" className="min-h-11 min-w-11" disabled={p.working} aria-label={t('staff.revokeNamed', { name: who })} onClick={() => p.onRevoke(m.invitationId!)}>{t('staff.revoke')}</Button>}
                        {canOffboard(m) && <Button type="button" variant="outline" className="min-h-11 min-w-11" disabled={p.working || !!p.offboarding} aria-label={t('staff.offboardNamed', { name: who })} onClick={() => p.onOffboard(m)}>{t('staff.offboard')}</Button>}
                      </span>
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </TableState>
        {!p.step && !p.loading && !p.failed && <Button type="button" className="self-start" onClick={p.onOpen}><Plus aria-hidden className="size-4" />{t('staff.add')}</Button>}
      </Section>

      {p.offboarding && (
        <Section title={t('staff.offboardTitle', { name: p.offboarding.name ?? p.offboarding.email ?? '' })} intro={t('staff.offboardIntro')}>
          <form noValidate onSubmit={p.onOffboardSubmit} className="flex flex-col gap-4">
            <Field id="staff-offboard-reason" label={t('staff.offboardReason')} error={p.offboardForm.formState.errors.reason && t('staff.offboardReasonRequired')}>
              <Textarea id="staff-offboard-reason" rows={3} maxLength={500} {...describedBy('staff-offboard-reason', !!p.offboardForm.formState.errors.reason)} {...p.offboardForm.register('reason')} />
            </Field>
            {p.offboardError && <p role="alert" className={fieldError}>{t(`staff.offboardError.${p.offboardError}`)}</p>}
            <div className="flex flex-wrap gap-2">
              <Button type="submit" variant="destructive" loading={p.offboardWorking}>{t('staff.offboardConfirm')}</Button>
              <Button type="button" variant="outline" onClick={p.onOffboardCancel}>{t('settings.cancel')}</Button>
            </div>
          </form>
        </Section>
      )}

      {p.step && (
        <Section title={t('staff.add')} intro={t('staff.stepOf', { n: stepIndex + 1, total: stepOrder.length, name: t(`staff.step.${p.step}`) })}>
          <form noValidate onSubmit={p.onSubmit} className="flex flex-col gap-4">
            {p.step === 'role' && (
              <fieldset className="flex flex-col gap-2">
                <legend className="mb-2 font-bold">{t('staff.step.role')}</legend>
                {choices.map(c => (
                  <label key={c.name + c.description} className="flex min-h-11 cursor-pointer items-start gap-3 rounded-lg border p-3 has-[:checked]:border-primary has-[:focus-visible]:ring-[3px] has-[:focus-visible]:ring-ring/50">
                    <input type="radio" name="staff-role" className="mt-1 size-4" checked={same(p.choice, c.choice)} onChange={() => p.onChoice(c.choice)} />
                    <span className="flex flex-col"><span className="font-bold">{c.name}</span><span className="text-sm text-muted-foreground">{c.description}</span></span>
                  </label>
                ))}
                {p.choice?.kind === 'doctor' && <p role="status" className="rounded-lg border border-warning-700 p-3 font-semibold">{t('staff.doctorNotice')}</p>}
              </fieldset>
            )}
            {p.step === 'scope' && (
              <div className="grid gap-4 sm:grid-cols-2">
                <Field id="staff-email" label={t('staff.email')} error={errors.email && t('settings.error.email')}>
                  <Input id="staff-email" type="email" autoComplete="off" className="h-11" {...describedBy('staff-email', !!errors.email)} {...p.form.register('email')} />
                </Field>
                <Field id="staff-facility" label={t('staff.facility')} hint={t('staff.hoursNote')}>
                  <Select value={p.form.watch('facilityId')} onValueChange={v => p.form.setValue('facilityId', v, { shouldValidate: true })}>
                    <SelectTrigger id="staff-facility" className="h-11 w-full"><SelectValue /></SelectTrigger>
                    <SelectContent>{p.locations.map(l => <SelectItem key={l.id} value={l.id}>{l.name}</SelectItem>)}</SelectContent>
                  </Select>
                </Field>
              </div>
            )}
            {p.step === 'permissions' && p.choice && (
              <div className="flex flex-col gap-2">
                <p className="text-muted-foreground">{t('staff.permissions.intro')}</p>
                <ul className="flex flex-col divide-y rounded-lg border">
                  {permissionRows(p.catalogue, p.choice).map(r => (
                    <li key={r.capability} className="flex flex-wrap items-center justify-between gap-2 p-3">
                      <span className="flex items-center gap-2 font-semibold"><Lock aria-hidden className="size-4 text-muted-foreground" />{t(`capability.${r.capability}`)}</span>
                      <span>{t(`access.${r.access}`)}{r.added && <span className="ms-2 text-sm text-muted-foreground">{t('staff.permissions.added')}</span>}</span>
                    </li>
                  ))}
                </ul>
                <p className="text-sm text-muted-foreground">{t('staff.permissions.locked')}</p>
              </div>
            )}
            {p.addFailed && <p role="alert" className={fieldError}>{t('staff.error.add')}</p>}
            {p.emailFailed && <p role="alert" className={fieldError}>{t('staff.error.email')}</p>}
            <div className="flex flex-wrap gap-2">
              {p.step !== 'role' && <Button type="button" variant="outline" onClick={p.onBack}>{t('staff.back')}</Button>}
              {p.step !== 'permissions' && <Button type="button" disabled={!p.canContinue} onClick={p.onNext}>{t('staff.next')}</Button>}
              {p.step === 'permissions' && <Button type="submit" loading={p.adding}>{t('staff.send')}</Button>}
              <Button type="button" variant="outline" onClick={p.onClose}>{t('settings.cancel')}</Button>
            </div>
          </form>
        </Section>
      )}
    </div>
  );
}

