import type { ReactNode } from 'react';
import { Building2, KeyRound, ShieldCheck } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, Card, PageHeader } from '@medvya/ui';
import type { Me, Stage } from '../domain/session';

type Props = {
  stage: Exclude<Stage, 'in'>;
  me: Me | null;
  hasLink: boolean;
  signOnFailed: boolean;
  keyFailed: boolean;
  busy: boolean;
  languageSwitcher: ReactNode;
  onSignOn: () => void;
  onTouchKey: () => void;
  onRegisterKey: () => void;
  onSignOut: () => void;
};

/** Sign-in to the platform console (design: Medvya Admin, sign-in): company sign-on, then the security key. No passwords. */
export default function SignInPage({ stage, me, hasLink, signOnFailed, keyFailed, busy, languageSwitcher, onSignOn, onTouchKey, onRegisterKey, onSignOut }: Props) {
  const { t } = useI18n();
  return (
    <main className="grid min-h-screen place-items-center bg-brand-950 p-4">
      {/* Labels wrap on a phone: a one-line "Continue with company sign-on" is wider than 375 px in Arabic (MDV-P2-059). */}
      <div className="flex w-full max-w-md flex-col gap-5 rounded-xl bg-card p-6 shadow-lg [&_[data-slot=button]]:whitespace-normal">
        {/* Wraps at 375 px: the brand line and the language picker side by side are wider than a phone (MDV-P2-059). */}
        <div className="flex flex-wrap items-center justify-between gap-3">
          <p className="text-xs font-bold tracking-[.09em] text-muted-foreground uppercase">{`${t('platform.brand.name')} · ${t('platform.brand.staffOnly')}`}</p>
          {languageSwitcher}
        </div>
        {/* The sign-in title wraps rather than truncates: nothing loads into this row, and on a phone the whole title must be read. */}
        <PageHeader className="[&_h1]:whitespace-normal" title={t('platform.signIn.title')} description={t('platform.signIn.lead')} />

        {stage === 'signedOut' && (
          <div className="flex flex-col gap-3">
            {hasLink && <p className="rounded-md bg-accent p-3 text-sm">{t('platform.signIn.linkReady')}</p>}
            {signOnFailed && <p role="alert" className="text-sm font-semibold text-danger-700">{t('platform.signIn.failed')}</p>}
            <Button onClick={onSignOn}><Building2 aria-hidden />{t('platform.signIn.company')}</Button>
          </div>
        )}

        {stage !== 'signedOut' && me && (
          <div className="flex flex-col gap-3">
            <p className="text-sm">{t('platform.signIn.as', { name: me.displayName, role: t(`platform.role.${me.role}`) })}</p>
            {stage === 'touchKey' && (
              <>
                <p className="font-semibold">{t('platform.signIn.touch')}</p>
                <Button onClick={onTouchKey} loading={busy}><KeyRound aria-hidden />{t('platform.signIn.useKey')}</Button>
              </>
            )}
            {stage === 'registerKey' && (
              <>
                <p className="font-semibold">{t('platform.signIn.register')}</p>
                <Button onClick={onRegisterKey} loading={busy}><KeyRound aria-hidden />{t('platform.signIn.registerKey')}</Button>
              </>
            )}
            {stage === 'askOwner' && <p role="status" className="font-semibold">{t('platform.signIn.noKey')}</p>}
            {keyFailed && <p role="alert" className="text-sm font-semibold text-danger-700">{t('platform.signIn.keyFailed')}</p>}
            <Button variant="outline" onClick={onSignOut}>{t('platform.action.signOut')}</Button>
          </div>
        )}

        <Card title={t('platform.signIn.lostTitle')} intro={t('platform.signIn.lostBody')} />
        <p className="flex items-start gap-2 text-sm text-muted-foreground">
          <ShieldCheck className="mt-0.5 size-4 shrink-0" aria-hidden />
          {t('platform.policy.long')}
        </p>
      </div>
    </main>
  );
}
