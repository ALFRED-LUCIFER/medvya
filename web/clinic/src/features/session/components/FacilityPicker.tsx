import type { ReactNode } from 'react';
import { useI18n } from '@medvya/i18n';
import { Button } from '@medvya/ui';
import { ChevronRight, ShieldCheck } from 'lucide-react';
import type { Membership } from '../domain/session';

type Props = {
  displayName: string;
  memberships: Membership[];
  choosingId: string | null;
  failed: boolean;
  languageSwitcher: ReactNode;
  signingOut: boolean;
  onChoose: (membershipId: string) => void;
  /** Only when switching away from a facility the user is already working at. */
  onCancel?: () => void;
  onSignOut: () => void;
};

/** F-03 step 3: "Signed in. Choose where you are working today." One row per active membership with its role. */
export default function FacilityPicker({ displayName, memberships, choosingId, failed, languageSwitcher, signingOut, onChoose, onCancel, onSignOut }: Props) {
  const { t } = useI18n();
  return (
    <main className="grid min-h-screen place-items-center p-4">
      <div className="flex w-full max-w-[480px] flex-col gap-4 rounded-lg border bg-card p-8">
        <div className="flex items-center gap-3">
          <ShieldCheck className="size-8 shrink-0 text-success-700" aria-hidden />
          <div>
            <h1 className="text-2xl font-bold">{t('shell.chooseFacility.title')}</h1>
            <p className="text-muted-foreground">{t('session.signedInAs', { name: displayName })}</p>
          </div>
        </div>
        {failed && <p role="alert" className="text-sm font-semibold text-danger-700">{t('session.picker.failed')}</p>}
        {memberships.length === 0
          ? <p role="status">{t('session.picker.none')}</p>
          : (
            <ul className="flex flex-col gap-2">
              {memberships.map(m => (
                <li key={m.id}>
                  <Button variant="outline" className="h-auto w-full justify-between gap-3 p-3.5 text-start whitespace-normal"
                    loading={choosingId === m.id} disabled={choosingId !== null} onClick={() => onChoose(m.id)}>
                    <span className="flex flex-col">
                      <span>{t('session.picker.facility', { tenant: m.tenantName, facility: m.facilityName })}</span>
                      <span className="text-[13px] font-normal text-muted-foreground">{t(`role.${m.role}`)}</span>
                    </span>
                    <ChevronRight className="size-5 text-muted-foreground rtl:rotate-180" aria-hidden />
                  </Button>
                </li>
              ))}
            </ul>
          )}
        <div className="flex flex-wrap gap-3">
          {onCancel && <Button variant="outline" onClick={onCancel}>{t('session.picker.cancel')}</Button>}
          <Button variant="outline" loading={signingOut} onClick={onSignOut}>{t('session.signOut')}</Button>
        </div>
        {languageSwitcher}
      </div>
    </main>
  );
}
