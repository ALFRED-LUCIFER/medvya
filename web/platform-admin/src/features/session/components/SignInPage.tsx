import type { ReactNode } from 'react';
import { Building2, KeyRound, ShieldCheck } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button } from '@medvya/ui';
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
      <div className="flex w-full max-w-md flex-col gap-5 rounded-xl bg-card p-6 shadow-lg">
        <div className="flex items-center justify-between gap-3">
          <p className="text-xs font-bold tracking-[.09em] text-muted-foreground uppercase">{`${t('platform.brand.name')} · ${t('platform.brand.staffOnly')}`}</p>
          {languageSwitcher}
        </div>
        <div className="flex flex-col gap-1">
          <h1 className="text-2xl font-extrabold">{t('platform.signIn.title')}</h1>
          <p className="text-sm text-muted-foreground">{t('platform.signIn.lead')}</p>
        </div>

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

        <section aria-labelledby="lost-key" className="flex flex-col gap-1 rounded-md border border-border p-3">
          <h2 id="lost-key" className="text-sm font-bold">{t('platform.signIn.lostTitle')}</h2>
          <p className="text-sm text-muted-foreground">{t('platform.signIn.lostBody')}</p>
        </section>
        <p className="flex items-start gap-2 text-sm text-muted-foreground">
          <ShieldCheck className="mt-0.5 size-4 shrink-0" aria-hidden />
          {t('platform.policy.long')}
        </p>
      </div>
    </main>
  );
}
