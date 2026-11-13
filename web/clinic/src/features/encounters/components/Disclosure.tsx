import type { ReactNode } from 'react';
import { ChevronDown } from 'lucide-react';
import { useI18n, type MessageKey } from '@medvya/i18n';

/**
 * A part of the visit the doctor opens only when needed (MDV audit P1-04): closed by default, open when it already holds something (so a placed order or open referral is never hidden), the title is the whole control. Native <details>
 * so the keyboard and screen readers work as they do elsewhere in the consultation (Safety checks, previous notes).
 */
export default function Disclosure({ titleKey, filled = false, children }: { titleKey: MessageKey; filled?: boolean; children: ReactNode }) {
  const { t } = useI18n();
  return (
    <details className="group" open={filled}>
      <summary className="flex min-h-11 cursor-pointer items-center justify-between gap-3 rounded-xl border bg-card px-4 text-lg font-extrabold focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring">
        {t(titleKey)}
        <ChevronDown aria-hidden className="size-5 shrink-0 transition-transform group-open:rotate-180" />
      </summary>
      <div className="mt-4">{children}</div>
    </details>
  );
}
