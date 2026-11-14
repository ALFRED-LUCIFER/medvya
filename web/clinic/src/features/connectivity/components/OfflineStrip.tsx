import { WifiOff } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { cn, toneClasses } from '@medvya/ui';

/**
 * Offline: a 40 px strip under the top bar (MDV-P2-056), in the page's flow, so it covers nothing and typed input survives (F-05d).
 * `note` is the screen's own words (the board: when it last updated); without one, what is true on every screen.
 */
export default function OfflineStrip({ note }: { note: string | null }) {
  const { t } = useI18n();
  return (
    <div role="status" className={cn('flex min-h-10 items-center gap-2 border-b px-4 py-2 text-body md:px-6 print:hidden', toneClasses.warning)}>
      <WifiOff aria-hidden className="size-4 shrink-0" />
      <p><strong>{t('offline.strip.title')}</strong> {note ?? t('offline.strip.body')}</p>
    </div>
  );
}
