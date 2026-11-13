import type { MouseEvent } from 'react';
import { Send } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { buttonVariants } from '@medvya/ui';

/** The referrals panel's heading id, so the plan's Referral link can move focus to it. */
export const referralsHeading = 'consult-referrals';

/** Referral in the plan (MDV-P2-043, design `fu`): moves to the visit's referrals, where the existing letter (MDV-P2-019) is written and printed. */
export default function ReferralJump() {
  const { t } = useI18n();
  const go = (e: MouseEvent<HTMLAnchorElement>) => {
    e.preventDefault();
    const heading = document.getElementById(referralsHeading);
    heading?.closest('details')?.setAttribute('open', ''); // the panel sits in a closed disclosure until used (MDV audit P1-04)
    heading?.focus();
  };
  return <a href={`#${referralsHeading}`} onClick={go} className={buttonVariants({ variant: 'outline', className: 'self-start' })}><Send aria-hidden className="size-4" />{t('plan.referral')}</a>;
}
