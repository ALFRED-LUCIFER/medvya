import { CircleCheck } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { cn, toneClasses } from '@medvya/ui';

/**
 * Back from the identity check (MDV-P2-047, #1365 F-062): "Identity confirmed. Press <action>.", right above the button it names, in words
 * with an icon. The live region is always on the page and only its words arrive, so a screen reader announces them (it takes the status
 * role only with words in it, so an empty one is not another status on the page); nothing is pressed for the doctor.
 */
export default function StepUpConfirmed({ confirmed, action }: { confirmed: boolean; action: string }) {
  const { t } = useI18n();
  return (
    <p aria-live="polite" role={confirmed ? 'status' : undefined} className={cn(confirmed && 'flex items-center gap-2 rounded-md border p-3 text-label', confirmed && toneClasses.success)}>
      {confirmed && <><CircleCheck aria-hidden className="size-5 shrink-0" />{t('stepUp.confirmed', { action })}</>}
    </p>
  );
}
