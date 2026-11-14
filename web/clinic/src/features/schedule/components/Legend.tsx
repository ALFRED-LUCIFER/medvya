import { useI18n } from '@medvya/i18n';
import { cn, scheduleLooks, type Look } from '@medvya/ui';
import { hatched } from './looks';

const legend = ['booked', 'checkedIn', 'inRoom', 'completed', 'noShow', 'cancelled', 'held'] as const;

/** What each look means, so states can be told apart by shape and words as well as colour. */
export function Legend({ className }: { className?: string }) {
  const { t } = useI18n();
  return (
    <ul aria-label={t('schedule.legend')} className={cn('flex flex-wrap gap-x-4 gap-y-1.5 text-xs text-muted-foreground', className)}>
      {legend.map(k => {
        const look: Look = scheduleLooks[k], Icon = look.icon;
        return (
          <li key={k} className="flex items-center gap-1.5">
            <span aria-hidden className="size-3.5 rounded-sm" style={{ backgroundColor: look.bg, border: look.border ?? `1px solid ${look.fg}` }} />
            <Icon aria-hidden className="size-3.5" />{t(look.label)}
          </li>
        );
      })}
      <li className="flex items-center gap-1.5">
        <span aria-hidden className={cn('size-3.5 rounded-sm border', hatched)} />
        <scheduleLooks.unavailable.icon aria-hidden className="size-3.5" />{t(scheduleLooks.unavailable.label)}
      </li>
    </ul>
  );
}
