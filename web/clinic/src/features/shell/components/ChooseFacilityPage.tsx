import type { ReactNode } from 'react';
import { useI18n } from '@medvya/i18n';
import { Button } from '@medvya/ui';

// Slot for the facility picker (MDV-P0-012): signed in, but /v1/me/context has no active membership yet.
export default function ChooseFacilityPage({ displayName, languageSwitcher, signingOut, onSignOut }:
  { displayName: string; languageSwitcher: ReactNode; signingOut: boolean; onSignOut: () => void }) {
  const { t } = useI18n();
  return (
    <main className="grid min-h-screen place-items-center p-4">
      <div className="flex w-full max-w-[480px] flex-col gap-4 rounded-lg border bg-card p-8">
        <h1 className="text-2xl font-bold">{t('shell.chooseFacility.title')}</h1>
        <p className="text-muted-foreground">{t('shell.chooseFacility.body', { name: displayName })}</p>
        <Button variant="outline" loading={signingOut} onClick={onSignOut}>{t('session.signOut')}</Button>
        {languageSwitcher}
      </div>
    </main>
  );
}
