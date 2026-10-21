import type { ReactNode } from 'react';
import { useI18n } from '@medvya/i18n';

export default function HomePage({ languageSwitcher, session }: { languageSwitcher: ReactNode; session: ReactNode }) {
  const { t } = useI18n();
  return (
    <main className="grid min-h-screen place-items-center p-4">
      <div className="flex w-full max-w-[480px] flex-col gap-4 rounded-lg border bg-card p-8">
        <h1 className="text-2xl font-bold">{t('app.name')}</h1>
        <p className="text-muted-foreground">{t('app.tagline')}</p>
        {session}
        {languageSwitcher}
      </div>
    </main>
  );
}
