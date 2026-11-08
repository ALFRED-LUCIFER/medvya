import type { ReactNode } from 'react';
import { useI18n } from '@medvya/i18n';
import { Button } from '@medvya/ui';
import { ChevronRight, Store } from 'lucide-react';
import type { Membership } from '../domain/session';
import AuthFrame from './AuthFrame';

type Props = {
  displayName: string;
  /** Last used first (toSession orders them). */
  memberships: Membership[];
  lastMembershipId: string | null;
  choosingId: string | null;
  failed: boolean;
  languageSwitcher: ReactNode;
  signingOut: boolean;
  onChoose: (membershipId: string) => void;
  /** Only when switching away from a facility the user is already working at. */
  onCancel?: () => void;
  onSignOut: () => void;
};

/** F-03 choose location (6 Nov design): one card per active membership with its role, the last used first and marked in words. */
export default function FacilityPicker({ displayName, memberships, lastMembershipId, choosingId, failed, languageSwitcher, signingOut, onChoose, onCancel, onSignOut }: Props) {
  const { t } = useI18n();
  return (
    <AuthFrame languageSwitcher={languageSwitcher}>
      <div className="flex flex-col gap-1.5">
        <h1 className="text-[28px] font-bold text-brand-950">{t('signin.loc.title')}</h1>
        <p className="text-[15px] text-muted-foreground">{t('signin.loc.body')}</p>
      </div>
      {failed && <p role="alert" className="text-sm font-semibold text-danger-700">{t('session.picker.failed')}</p>}
      {memberships.length === 0
        ? <p role="status">{t('session.picker.none')}</p>
        : (
          <ul className="flex flex-col gap-2.5">
            {memberships.map(m => (
              <li key={m.id}>
                <Button variant="outline" className={`h-auto w-full justify-between gap-3 rounded-xl p-3.5 text-start whitespace-normal ${m.id === lastMembershipId ? 'border-2 border-brand-700' : ''}`}
                  loading={choosingId === m.id} disabled={choosingId !== null} onClick={() => onChoose(m.id)}>
                  <span className="grid size-10 shrink-0 place-items-center rounded-lg bg-brand-50 text-brand-700"><Store className="size-5" aria-hidden /></span>
                  <span className="flex min-w-0 flex-1 flex-col">
                    <span>{t('session.picker.facility', { tenant: m.tenantName, facility: m.facilityName })}</span>
                    <span className="text-[13px] font-normal text-muted-foreground">{t(`role.${m.role}`)}</span>
                  </span>
                  {m.id === lastMembershipId && <span className="rounded-full bg-success-50 px-2.5 py-0.5 text-xs font-bold text-success-700">{t('signin.loc.lastUsed')}</span>}
                  <ChevronRight className="size-5 text-muted-foreground rtl:rotate-180" aria-hidden />
                </Button>
              </li>
            ))}
          </ul>
        )}
      <div className="flex flex-wrap items-center justify-between gap-2 text-sm text-muted-foreground">
        <span>{t('session.signedInAs', { name: displayName })}</span>
        <Button variant="ghost" loading={signingOut} onClick={onSignOut}>{t('signin.notYou')}</Button>
      </div>
      {onCancel && <Button variant="outline" onClick={onCancel}>{t('session.picker.cancel')}</Button>}
    </AuthFrame>
  );
}
