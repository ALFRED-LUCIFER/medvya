import type { ReactNode } from 'react';
import { useI18n } from '@medvya/i18n';
import { Button, PageHeader } from '@medvya/ui';
import { LogOut } from 'lucide-react';
import AuthFrame from './AuthFrame';

type Props = { languageSwitcher: ReactNode; onSignIn: () => void; onOtherAccount: () => void };

/**
 * F-05a signed out / session ended (6 Nov design). Signing in again returns to the current route (startSignIn keeps the path).
 * shortcut: no drafts exist yet, so nothing is restored and no patient is re-confirmed; wire both when the first draft-bearing screen lands (notes, MDV-P2).
 */
export default function SessionExpiredPage({ languageSwitcher, onSignIn, onOtherAccount }: Props) {
  const { t } = useI18n();
  return (
    <AuthFrame languageSwitcher={languageSwitcher}>
      <section role="alert" aria-label={t('signin.ended.title')} className="flex flex-col gap-[18px]">
        <span className="grid size-12 place-items-center rounded-full bg-muted text-ink-700"><LogOut className="size-6 rtl:rotate-180" aria-hidden /></span>
        {/* Wraps rather than truncates: nothing loads into this title, and on a phone it must be read whole. */}
        <PageHeader className="[&_h1]:whitespace-normal" title={t('signin.ended.title')} description={t('signin.ended.body')} />
        <Button className="min-h-12" onClick={onSignIn}>{t('signin.ended.cta')}</Button>
        <Button variant="ghost" onClick={onOtherAccount}>{t('signin.ended.other')}</Button>
      </section>
    </AuthFrame>
  );
}
