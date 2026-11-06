import { useI18n } from '@medvya/i18n';
import { cn, scheduleLooks } from '@medvya/ui';

/** A free slot that has already started: the unavailable look (hatching, icon, words), not a button; its name says it has passed. */
export function PastSlot({ time, className }: { time: string; className?: string }) {
  const { t } = useI18n();
  const look = scheduleLooks.unavailable;
  return (
    <span className={cn('flex items-center gap-1 text-xs font-bold', className)} style={{ color: look.fg }}>
      <look.icon aria-hidden className="size-3.5 shrink-0" /><span aria-hidden>{t('schedule.slot.passed')}</span>
      <span className="sr-only">{t('schedule.slot.passedAt', { time })}</span>
    </span>
  );
}
