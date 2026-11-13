import type { ReactNode } from 'react';
import { useI18n } from '@medvya/i18n';
import { Card } from '@medvya/ui';

/** Page frame shared by the sign-up screens: brand, language switcher and one card. */
export default function Frame({ languageSwitcher, children }: { languageSwitcher: ReactNode; children: ReactNode }) {
  const { t } = useI18n();
  return (
    <main className="mx-auto flex min-h-screen w-full max-w-[720px] flex-col gap-6 p-4 md:p-8">
      <div className="flex items-center justify-between gap-3">
        <span className="text-lg font-extrabold">{t('app.name')}</span>
        {languageSwitcher}
      </div>
      <Card className="gap-6 p-5 md:p-7">{children}</Card>
    </main>
  );
}
