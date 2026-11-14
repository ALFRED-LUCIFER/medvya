import { ChevronDown, Lock, Plus } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, ChoiceRow, DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger, Field, Input, Table, TableBody, TableCell, TableHead, TableHeader, TablePagination, TableRow, TableState, Textarea } from '@medvya/ui';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@medvya/ui';
import type { FormEventHandler } from 'react';
import type { UseFormReturn } from 'react-hook-form';
import { fieldError } from '../../settings/components/fieldStyles';
import type { Location } from '../../settings/domain/settings';
import { Section } from '../../settings/components/Section';
import type { TableView } from '../../table/domain/tableSort';
import { staffActions, staffColumns, type StaffAction, type StaffSortColumn } from '../domain/staffTable';
import { isOwnerAmong, offboardBlock, roleValue, assignableRoles, canOffboard, locationsOf, isDoctor, permissionRows, statusLabel, type AddStaffForm, type LocationsForm, type OffboardForm, type ReactivateForm, type RoleCatalogue, type RoleChoice, type StaffMember, type Step } from '../domain/staff';

type Props = {
  /** Everyone, for the rules that look across rows (owner, locations); `table.rows` is the sorted page shown. */
  staff: StaffMember[];
  table: TableView<StaffMember, StaffSortColumn>;
  loading: boolean;
  failed: boolean;
  catalogue: RoleCatalogue;
  locations: Location[];
  step: Step | null;
  choice: RoleChoice | null;
  form: UseFormReturn<AddStaffForm>;
  canContinue: boolean;
  notice: 'invited' | 'resent' | 'revoked' | 'offboarded' | 'reactivated' | 'locations' | 'role' | null;
  /** The signed-in user: no Offboard or Change role on their own row (MDV-P1-107). */
  currentUserId: string;
  openVisits: number;
  offboarding: StaffMember | null;
  offboardForm: UseFormReturn<OffboardForm>;
  offboardWorking: boolean;
  offboardError: 'reauth' | 'ownerOnly' | 'lastOwner' | 'lastAdmin' | 'failed' | null;
  stepUpFailed: boolean;
  onStepUp: () => void;
  changing: StaffMember | null;
  newRole: string;
  changeRoleWorking: boolean;
  changeRoleError: 'lastAdmin' | 'failed' | null;
  onChangeRole: (member: StaffMember) => void;
  onNewRole: (value: string) => void;
  onChangeRoleSubmit: FormEventHandler<HTMLFormElement>;
  onChangeRoleCancel: () => void;
  onOffboard: (member: StaffMember) => void;
  onOffboardSubmit: FormEventHandler<HTMLFormElement>;
  onOffboardCancel: () => void;
  locating: StaffMember | null;
  locationsForm: UseFormReturn<LocationsForm>;
  locationsWorking: boolean;
  locationsError: 'lastAdmin' | 'failed' | null;
  onLocations: (member: StaffMember) => void;
  onLocationsSubmit: FormEventHandler<HTMLFormElement>;
  onLocationsCancel: () => void;
  reactivating: StaffMember | null;
  reactivateForm: UseFormReturn<ReactivateForm>;
  reactivateWorking: boolean;
  reactivateError: 'aboveGranter' | 'planFull' | 'failed' | null;
  onReactivate: (member: StaffMember) => void;
  onReactivateSubmit: FormEventHandler<HTMLFormElement>;
  onReactivateCancel: () => void;
  adding: boolean;
  addFailed: boolean;
  emailFailed: boolean;
  planFull: boolean;
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
  const { t, formatDate, dir } = useI18n();
  const { errors } = p.form.formState;
  const tableState = p.loading ? 'loading' : p.failed ? 'error' : p.staff.length === 0 ? 'empty' : 'ready';

  const roleName = (m: StaffMember) => m.customRoleName ?? t(`role.${m.role}`);
  const choices: { choice: RoleChoice; name: string; description: string }[] = [
    ...assignableRoles.map(role => ({ choice: { kind: 'builtIn', role } as RoleChoice, name: t(`role.${role}`), description: t(`staff.roleDesc.${role}`) })),
    ...p.catalogue.custom.map(c => ({ choice: { kind: 'custom', id: c.id } as RoleChoice, name: c.name, description: t('staff.roleDesc.custom', { base: t(`role.${c.baseRole}`) }) })),
    { choice: { kind: 'doctor' }, name: t('role.HospitalDoctor'), description: t('staff.roleDesc.Doctor') },
  ];
  const stepIndex = p.step ? stepOrder.indexOf(p.step) : -1;
  const iAmOwner = isOwnerAmong(p.staff, p.currentUserId);

  return (
    <div className="flex flex-col gap-4">
      <Section title={t('staff.title')} intro={t('staff.intro')}>
        <div aria-live="polite" className="text-sm font-semibold text-success-700">
          {p.notice && t(`staff.notice.${p.notice}`)}
          {p.notice === 'offboarded' && p.openVisits > 0 && <span className="block text-foreground">{t('staff.notice.openVisits', { count: p.openVisits })}</span>}
        </div>
        {p.rowFailed && <p role="alert" className={fieldError}>{t('staff.rowFailed')}</p>}
        {p.emailFailed && !p.adding && <p role="alert" className={fieldError}>{t('staff.error.email')}</p>}
        <TableState state={tableState} messages={{ loading: t('settings.loading'), error: t('settings.loadFailed'), empty: t('staff.empty') }}>
          {/* Compact: a reference list read with a mouse (DESIGN.md, Density); stacked under 768 px with the name first. */}
          <Table label={t('staff.tableLabel')} density="compact" stacked status={p.table.status} className="md:min-w-[720px]">
            <TableHeader>
              <TableRow>
                {staffColumns.map(c => (
                  <TableHead key={c.key} scope="col" sort={c.sortable ? p.table.sortOf(c.key) : undefined} onSort={c.sortable ? () => p.table.onSort(c.key) : undefined}>{t(`staff.col.${c.key}`)}</TableHead>
                ))}
                <TableHead scope="col"><span className="sr-only">{t('staff.col.actions')}</span></TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {p.table.rows.map(m => {
                const who = m.name ?? m.email ?? '';
                const blocked = canOffboard(m) ? offboardBlock(m, p.currentUserId, iAmOwner) : null;
                const actions = staffActions(m, p.currentUserId, iAmOwner, p.locations.length > 1);
                const act: Record<StaffAction, { run: () => void; disabled: boolean }> = {
                  changeRole: { run: () => p.onChangeRole(m), disabled: p.working || !!p.changing },
                  locations: { run: () => p.onLocations(m), disabled: p.working || !!p.locating },
                  reactivate: { run: () => p.onReactivate(m), disabled: p.working || !!p.reactivating },
                  resend: { run: () => p.onResend(m.invitationId!), disabled: p.working },
                  revoke: { run: () => p.onRevoke(m.invitationId!), disabled: p.working },
                  offboard: { run: () => p.onOffboard(m), disabled: p.working || !!p.offboarding },
                };
                return (
                  <TableRow key={m.membershipId}>
                    <TableHead scope="row">{who}</TableHead>
                    <TableCell label={t('staff.col.role')}>{roleName(m)}</TableCell>
                    <TableCell label={t('staff.col.access')}>{m.facilityName}</TableCell>
                    <TableCell label={t('staff.col.mfa')}>{t(`staff.mfa.${m.mfa}`)}</TableCell>
                    <TableCell label={t('staff.col.status')}>
                      {t(`staff.status.${statusLabel(m)}`)}
                      {m.invitationState === 'pending' && <span className="block text-muted-foreground">{t(`staff.invitation.${m.invitationState}`, { date: m.invitationExpiresAt ? formatDate(new Date(m.invitationExpiresAt)) : '' })}</span>}
                    </TableCell>
                    <TableCell>
                      <span className="flex flex-wrap items-center gap-2">
                        {actions.visible && (
                          <Button type="button" variant="outline" className="min-w-11" disabled={act[actions.visible].disabled} aria-label={t(`staff.${actions.visible}Named`, { name: who })} onClick={act[actions.visible].run}>
                            {t(`staff.${actions.visible}`)}
                          </Button>
                        )}
                        {actions.menu.length > 0 && (
                          <DropdownMenu dir={dir} modal={false}>
                            <DropdownMenuTrigger asChild>
                              <Button type="button" variant="outline" disabled={p.working} aria-label={t('staff.actionsNamed', { name: who })}>{t('staff.col.actions')}<ChevronDown aria-hidden /></Button>
                            </DropdownMenuTrigger>
                            {/* The offboarding form takes focus (#912): the menu does not trap it, nor hand it back to its button. */}
                            <DropdownMenuContent align="end" onCloseAutoFocus={e => { if (p.offboarding) e.preventDefault(); }}>
                              {actions.menu.map(a => (
                                <DropdownMenuItem key={a} disabled={act[a].disabled} aria-label={t(`staff.${a}Named`, { name: who })} onSelect={act[a].run}>{t(`staff.${a}`)}</DropdownMenuItem>
                              ))}
                            </DropdownMenuContent>
                          </DropdownMenu>
                        )}
                        {/* MDV-P1-107: no Offboard where this user may not offboard; the reason in words instead. The server refuses the same. */}
                        {blocked && <span className="text-caption text-muted-foreground">{t(`staff.offboardBlocked.${blocked}`)}</span>}
                      </span>
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
          <TablePagination page={p.table.page} pageCount={p.table.pageCount} onPage={p.table.onPage} />
        </TableState>
        {!p.step && !p.loading && !p.failed && <Button type="button" className="self-start" onClick={p.onOpen}><Plus aria-hidden className="size-4" />{t('staff.add')}</Button>}
      </Section>

      {p.offboarding && (
        <Section title={t('staff.offboardTitle', { name: p.offboarding.name ?? p.offboarding.email ?? '' })} intro={t('staff.offboardIntro')}>
          <form noValidate onSubmit={p.onOffboardSubmit} className="flex flex-col gap-4">
            <Field id="staff-offboard-reason" label={t('staff.offboardReason')} error={p.offboardForm.formState.errors.reason && t('staff.offboardReasonRequired')}>
              <Textarea autoFocus rows={3} maxLength={500} {...p.offboardForm.register('reason')} />
            </Field>
            {isDoctor(p.offboarding) && (
              <fieldset className="flex flex-col gap-2" aria-describedby={p.offboardForm.formState.errors.rebooking ? 'staff-offboard-rebooking-error' : undefined}>
                <legend className="mb-1 font-bold">{t('staff.offboardRebooking')}</legend>
                {(['offer', 'call'] as const).map(choice => (
                  <ChoiceRow key={choice} className="items-start gap-3 rounded-lg border p-3 has-[:checked]:border-primary has-[:focus-visible]:ring-[3px] has-[:focus-visible]:ring-ring/50 [&>input]:mt-1" type="radio" value={choice} {...p.offboardForm.register('rebooking')}>
                    <span className="flex flex-col"><span className="font-bold">{t(`staff.offboardRebooking.${choice}`)}</span><span className="text-sm text-muted-foreground">{t(`staff.offboardRebooking.${choice}.hint`)}</span></span>
                  </ChoiceRow>
                ))}
                {p.offboardForm.formState.errors.rebooking && <p id="staff-offboard-rebooking-error" role="alert" className={fieldError}>{t('staff.offboardRebooking.required')}</p>}
              </fieldset>
            )}
            {p.offboardError && <p role="alert" className={fieldError}>{t(`staff.offboardError.${p.offboardError}`)}</p>}
            {p.offboardError === 'reauth' && <Button type="button" variant="outline" className="self-start" onClick={p.onStepUp}>{t('staff.stepUp')}</Button>}
            {p.stepUpFailed && <p role="alert" className={fieldError}>{t('staff.stepUpFailed')}</p>}
            <div className="flex flex-wrap gap-2">
              <Button type="submit" variant="destructive" loading={p.offboardWorking}>{t('staff.offboardConfirm')}</Button>
              <Button type="button" variant="outline" onClick={p.onOffboardCancel}>{t('settings.cancel')}</Button>
            </div>
          </form>
        </Section>
      )}

      {p.locating && (() => {
        const chosen = p.locationsForm.watch('facilityIds');
        const current = locationsOf(p.staff, p.locating);
        const removing = current.some(id => !chosen.includes(id));
        const toggle = (id: string) => p.locationsForm.setValue('facilityIds', chosen.includes(id) ? chosen.filter(x => x !== id) : [...chosen, id], { shouldValidate: true });
        return (
          <Section title={t('staff.locationsTitle', { name: p.locating.name ?? p.locating.email ?? '' })} intro={t('staff.locationsIntro')}>
            <form noValidate onSubmit={p.onLocationsSubmit} className="flex flex-col gap-4">
              <fieldset className="flex flex-col gap-2" aria-describedby={p.locationsForm.formState.errors.facilityIds ? 'staff-locations-error' : undefined}>
                <legend className="mb-1 font-bold">{t('staff.facility')}</legend>
                {p.locations.map(l => (
                  <ChoiceRow key={l.id} className="gap-3 rounded-lg border p-3 has-[:checked]:border-primary has-[:focus-visible]:ring-[3px] has-[:focus-visible]:ring-ring/50" type="checkbox" checked={chosen.includes(l.id)} onChange={() => toggle(l.id)}>
                    <span className="font-semibold">{l.name}</span>
                  </ChoiceRow>
                ))}
                {p.locationsForm.formState.errors.facilityIds && <p id="staff-locations-error" role="alert" className={fieldError}>{t('staff.locationsRequired')}</p>}
              </fieldset>
              {removing && (
                <Field id="staff-locations-reason" label={t('staff.offboardReason')} hint={t('staff.locationsReasonHint')} error={p.locationsForm.formState.errors.reason && t('staff.offboardReasonRequired')}>
                  <Textarea rows={3} maxLength={500} {...p.locationsForm.register('reason')} />
                </Field>
              )}
              {p.locationsError && <p role="alert" className={fieldError}>{t(`staff.locationsError.${p.locationsError}`)}</p>}
              <div className="flex flex-wrap gap-2">
                <Button type="submit" loading={p.locationsWorking}>{t('staff.locationsConfirm')}</Button>
                <Button type="button" variant="outline" onClick={p.onLocationsCancel}>{t('settings.cancel')}</Button>
              </div>
            </form>
          </Section>
        );
      })()}

      {p.changing && (
        <Section title={t('staff.changeRoleTitle', { name: p.changing.name ?? p.changing.email ?? '' })} intro={t('staff.changeRoleIntro')}>
          <form noValidate onSubmit={p.onChangeRoleSubmit} className="flex flex-col gap-4">
            <Select value={p.newRole} onValueChange={p.onNewRole}>
              <Field id="staff-change-role" label={t('staff.changeRoleTo')}>
                <SelectTrigger className="w-full"><SelectValue /></SelectTrigger>
              </Field>
              <SelectContent>
                {assignableRoles.map(r => <SelectItem key={r} value={`builtIn:${r}`}>{t(`role.${r}`)}</SelectItem>)}
                {p.catalogue.custom.map(c => <SelectItem key={c.id} value={`custom:${c.id}`}>{c.name}</SelectItem>)}
              </SelectContent>
            </Select>
            {p.changeRoleError && <p role="alert" className={fieldError}>{t(`staff.changeRoleError.${p.changeRoleError}`)}</p>}
            <div className="flex flex-wrap gap-2">
              <Button type="submit" loading={p.changeRoleWorking} disabled={p.newRole === roleValue(p.changing)}>{t('staff.changeRoleConfirm')}</Button>
              <Button type="button" variant="outline" onClick={p.onChangeRoleCancel}>{t('settings.cancel')}</Button>
            </div>
          </form>
        </Section>
      )}

      {p.reactivating && (
        <Section title={t('staff.reactivateTitle', { name: p.reactivating.name ?? p.reactivating.email ?? '' })} intro={t('staff.reactivateIntro')}>
          <form noValidate onSubmit={p.onReactivateSubmit} className="flex flex-col gap-4">
            <Field id="staff-reactivate-reason" label={t('staff.offboardReason')} error={p.reactivateForm.formState.errors.reason && t('staff.offboardReasonRequired')}>
              <Textarea rows={3} maxLength={500} {...p.reactivateForm.register('reason')} />
            </Field>
            {p.reactivateError && <p role="alert" className={fieldError}>{t(`staff.reactivateError.${p.reactivateError}`)}</p>}
            <div className="flex flex-wrap gap-2">
              <Button type="submit" loading={p.reactivateWorking}>{t('staff.reactivateConfirm')}</Button>
              <Button type="button" variant="outline" onClick={p.onReactivateCancel}>{t('settings.cancel')}</Button>
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
                  <ChoiceRow key={c.name + c.description} className="items-start gap-3 rounded-lg border p-3 has-[:checked]:border-primary has-[:focus-visible]:ring-[3px] has-[:focus-visible]:ring-ring/50 [&>input]:mt-1" type="radio" name="staff-role" checked={same(p.choice, c.choice)} onChange={() => p.onChoice(c.choice)}>
                    <span className="flex flex-col"><span className="font-bold">{c.name}</span><span className="text-sm text-muted-foreground">{c.description}</span></span>
                  </ChoiceRow>
                ))}
                {p.choice?.kind === 'doctor' && <p role="status" className="rounded-lg border border-warning-700 p-3 font-semibold">{t('staff.doctorNotice')}</p>}
              </fieldset>
            )}
            {p.step === 'scope' && (
              <div className="grid gap-4 sm:grid-cols-2">
                <Field id="staff-email" label={t('staff.email')} error={errors.email && t('settings.error.email')}>
                  <Input type="email" autoComplete="off" {...p.form.register('email')} />
                </Field>
                <Select value={p.form.watch('facilityId')} onValueChange={v => p.form.setValue('facilityId', v, { shouldValidate: true })}>
                  <Field id="staff-facility" label={t('staff.facility')} hint={t('staff.hoursNote')}>
                    <SelectTrigger className="w-full"><SelectValue /></SelectTrigger>
                  </Field>
                  <SelectContent>{p.locations.map(l => <SelectItem key={l.id} value={l.id}>{l.name}</SelectItem>)}</SelectContent>
                </Select>
                {p.locations.length > 1 && (
                  <fieldset className="flex flex-col gap-2 sm:col-span-2">
                    <legend className="mb-1 font-bold">{t('staff.alsoAt')}</legend>
                    {p.locations.filter(l => l.id !== p.form.watch('facilityId')).map(l => {
                      const also = p.form.watch('alsoFacilityIds');
                      return (
                        <ChoiceRow key={l.id} className="gap-3 rounded-lg border p-3 has-[:checked]:border-primary has-[:focus-visible]:ring-[3px] has-[:focus-visible]:ring-ring/50" type="checkbox" checked={also.includes(l.id)}
                            onChange={() => p.form.setValue('alsoFacilityIds', also.includes(l.id) ? also.filter(x => x !== l.id) : [...also, l.id])}>
                          <span className="font-semibold">{l.name}</span>
                        </ChoiceRow>
                      );
                    })}
                    <p className="text-sm text-muted-foreground">{t('staff.alsoAtHint')}</p>
                  </fieldset>
                )}
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
            {p.planFull && <p role="alert" className={fieldError}>{t('plan.full')}</p>}
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

