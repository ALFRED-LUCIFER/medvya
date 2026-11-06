import { Link } from 'react-router';
import { Info } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { termsPath } from '../domain/settings';

/** Onboarding prompt for the practice owner: booking and messages stay off until the terms are accepted (MDV-P1-019). */
export default function TermsNotice() {
  const { t } = useI18n();
  return (
    <p role="note" className="flex flex-wrap items-start gap-2 border-b bg-muted px-4 py-2 text-sm">
      <Info className="mt-0.5 size-4 shrink-0" aria-hidden />{t('terms.notice')}
      <Link to={termsPath} className="font-bold underline">{t('terms.open')}</Link>
    </p>
  );
}
