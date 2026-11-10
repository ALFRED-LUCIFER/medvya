import type { FormEventHandler, ReactNode } from 'react';
import type { UseFormReturn } from 'react-hook-form';
import { CircleAlert, CircleCheck, FileDown, MessageSquare } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, Input } from '@medvya/ui';
import AuthFrame from '../../session/components/AuthFrame';
import { describedBy } from '../../settings/components/fieldStyles';
import { Field } from '../../settings/components/Field';
import type { CodeForm, OpenProblem } from '../domain/share';

type Props = {
  form: UseFormReturn<CodeForm>; problem: OpenProblem | null;
  codeSent: boolean; sending: boolean; opening: boolean; opened: boolean;
  onSendCode: () => void; onOpen: FormEventHandler<HTMLFormElement>; languageSwitcher: ReactNode;
};

/**
 * The page a shared prescription link opens (MDV-P2-020), public: it shows nothing until the code sent to the patient's registered mobile is entered,
 * then saves the PDF. A pharmacy asks the patient for the code. Problems are in words with an icon, never colour alone.
 */
export default function SharedRxPage(p: Props) {
  const { t } = useI18n();
  const invalid = !!p.form.formState.errors.code;
  return (
    <AuthFrame languageSwitcher={p.languageSwitcher}>
      <section aria-labelledby="shared-rx-title" className="flex flex-col gap-3">
        <h1 id="shared-rx-title" className="text-xl font-extrabold">{t('sharedRx.title')}</h1>
        <p>{t('sharedRx.intro')}</p>
        <Button type="button" variant={p.codeSent ? 'outline' : 'default'} className="min-h-11 self-start" loading={p.sending} onClick={p.onSendCode}>
          <MessageSquare aria-hidden className="size-4" />{t(p.codeSent ? 'sharedRx.resend' : 'sharedRx.sendCode')}
        </Button>
        {p.codeSent && <p role="status" className="flex items-center gap-2"><CircleCheck aria-hidden className="size-4 shrink-0" />{t('sharedRx.codeSent')}</p>}
        <form className="flex flex-wrap items-end gap-3" onSubmit={p.onOpen} noValidate>
          <Field id="shared-rx-code" label={t('sharedRx.code')} error={invalid ? t('sharedRx.codeInvalid') : null} className="min-w-48">
            <Input id="shared-rx-code" inputMode="numeric" autoComplete="one-time-code" dir="ltr" maxLength={6} className="h-11" {...describedBy('shared-rx-code', invalid)} {...p.form.register('code')} />
          </Field>
          <Button type="submit" className="min-h-11" loading={p.opening}><FileDown aria-hidden className="size-4" />{t('sharedRx.open')}</Button>
        </form>
        {p.opened && <p role="status" className="flex items-center gap-2"><CircleCheck aria-hidden className="size-4 shrink-0" />{t('sharedRx.opened')}</p>}
        {p.problem && <p role="alert" className="flex items-start gap-2 font-bold"><CircleAlert aria-hidden className="mt-0.5 size-5 shrink-0" />{t(`sharedRx.problem.${p.problem}`)}</p>}
        <p className="text-sm text-muted-foreground">{t('sharedRx.privacy')}</p>
      </section>
    </AuthFrame>
  );
}
