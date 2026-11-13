import type { FormEventHandler, ReactNode } from 'react';
import type { UseFormReturn } from 'react-hook-form';
import { Link } from 'react-router';
import { useI18n } from '@medvya/i18n';
import { Button, buttonVariants, Input, Label, PageHeader } from '@medvya/ui';
import { Building2, CircleAlert, KeyRound } from 'lucide-react';
import { securityInfoUrl, type SignInFailure, type SignInForm } from '../domain/signIn';
import AuthFrame from './AuthFrame';

type Props = {
  languageSwitcher: ReactNode;
  form: UseFormReturn<SignInForm>;
  failure: SignInFailure | null;
  onSubmit: FormEventHandler<HTMLFormElement>;
  onWithoutEmail: () => void;
};

/** F-03 sign-in start (6 Nov design). Everything after Continue (email code, passkey, MFA) is the provider's page. */
export default function SignInPage({ languageSwitcher, form, failure, onSubmit, onWithoutEmail }: Props) {
  const { t } = useI18n();
  const error = form.formState.errors.email;
  return (
    <AuthFrame languageSwitcher={languageSwitcher}>
      {/* Wraps rather than truncates: nothing loads into this title, and on a phone it must be read whole. */}
      <PageHeader className="[&_h1]:whitespace-normal" title={t('signin.title')} description={t('signin.body')} />
      {failure === 'failed' && (
        <p role="alert" className="flex items-start gap-2 rounded-lg border border-danger-700 bg-danger-50 p-3 text-sm font-semibold text-danger-700">
          <CircleAlert className="mt-0.5 size-4 shrink-0" aria-hidden />{t('session.failed')}
        </p>
      )}
      {failure === 'mfa' && (
        <div role="alert" className="flex items-start gap-2 rounded-lg border border-danger-700 bg-danger-50 p-3 text-sm text-danger-700">
          <CircleAlert className="mt-0.5 size-4 shrink-0" aria-hidden />
          <div className="flex flex-col gap-1">
            <p className="font-semibold">{t('session.failedMfa')}</p>
            <p>{t('session.failedMfa.how')}</p>
            <a href={securityInfoUrl} target="_blank" rel="noreferrer" className="min-h-11 content-center font-semibold underline">{t('session.failedMfa.link')}</a>
            <p>{t('session.failedMfa.admin')}</p>
          </div>
        </div>
      )}
      <form noValidate onSubmit={onSubmit} className="flex flex-col gap-[18px]">
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="signin-email">{t('signin.email')}</Label>
          <Input id="signin-email" type="email" dir="ltr" autoComplete="username" inputMode="email" spellCheck={false}
            aria-invalid={error ? true : undefined} aria-describedby={error ? 'signin-email-error' : undefined} {...form.register('email')} />
          {error && (
            <p id="signin-email-error" role="alert" className="flex items-center gap-1.5 text-sm font-semibold text-danger-700">
              <CircleAlert className="size-4 shrink-0" aria-hidden />{t('signin.email.invalid')}
            </p>
          )}
        </div>
        <Button type="submit" className="min-h-12">{t('signin.continue')}</Button>
      </form>
      <div className="flex items-center gap-3 text-sm text-muted-foreground">
        <span aria-hidden className="h-px flex-1 bg-border" />{t('signin.or')}<span aria-hidden className="h-px flex-1 bg-border" />
      </div>
      <Button variant="outline" onClick={onWithoutEmail}><KeyRound className="size-5" aria-hidden />{t('signin.passkey')}</Button>
      <Button variant="outline" onClick={onWithoutEmail}><Building2 className="size-5" aria-hidden />{t('signin.sso')}</Button>
      <p className="flex flex-wrap items-center gap-x-2 text-sm text-muted-foreground">
        {t('signin.newPractice')}
        <Link to="/signup" className={buttonVariants({ variant: 'link', className: 'px-0' })}>{t('signin.createPractice')}</Link>
      </p>
    </AuthFrame>
  );
}
