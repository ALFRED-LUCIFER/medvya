import type { FormEventHandler, ReactNode } from 'react';
import type { UseFormReturn } from 'react-hook-form';
import { Link } from 'react-router';
import { useI18n } from '@medvya/i18n';
import { Button, buttonVariants, Input, Label } from '@medvya/ui';
import { Building2, CircleAlert, KeyRound } from 'lucide-react';
import type { SignInForm } from '../domain/signIn';
import AuthFrame from './AuthFrame';

type Props = {
  languageSwitcher: ReactNode;
  form: UseFormReturn<SignInForm>;
  failed: boolean;
  onSubmit: FormEventHandler<HTMLFormElement>;
  onWithoutEmail: () => void;
};

/** F-03 sign-in start (6 Nov design). Everything after Continue (email code, passkey, MFA) is the provider's page. */
export default function SignInPage({ languageSwitcher, form, failed, onSubmit, onWithoutEmail }: Props) {
  const { t } = useI18n();
  const error = form.formState.errors.email;
  return (
    <AuthFrame languageSwitcher={languageSwitcher}>
      <div className="flex flex-col gap-1.5">
        <h1 className="text-[28px] font-bold text-brand-950">{t('signin.title')}</h1>
        <p className="text-[15px] text-muted-foreground">{t('signin.body')}</p>
      </div>
      {failed && (
        <p role="alert" className="flex items-start gap-2 rounded-lg border border-danger-700 bg-danger-50 p-3 text-sm font-semibold text-danger-700">
          <CircleAlert className="mt-0.5 size-4 shrink-0" aria-hidden />{t('session.failed')}
        </p>
      )}
      <form noValidate onSubmit={onSubmit} className="flex flex-col gap-[18px]">
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="signin-email">{t('signin.email')}</Label>
          <Input id="signin-email" type="email" dir="ltr" autoComplete="username" inputMode="email" spellCheck={false} className="min-h-11"
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
        <Link to="/signup" className={buttonVariants({ variant: 'link', className: 'min-h-11 px-0' })}>{t('signin.createPractice')}</Link>
      </p>
    </AuthFrame>
  );
}
