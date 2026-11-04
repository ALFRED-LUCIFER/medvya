import { Info } from 'lucide-react';
import { useI18n } from '@medvya/i18n';

/** The designed "Verification pending" notice, shown to a doctor whose recording and signing are off until the licence is verified. */
export default function LicenceNotice() {
  const { t } = useI18n();
  return (
    <p role="note" className="flex items-start gap-2 border-b bg-muted px-4 py-2 text-sm">
      <Info className="mt-0.5 size-4 shrink-0" aria-hidden />{t('onboarding.licence.notice')}
    </p>
  );
}
