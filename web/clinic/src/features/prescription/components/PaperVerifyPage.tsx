import type { ReactNode } from 'react';
import { CircleAlert, ShieldCheck } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import AuthFrame from '../../session/components/AuthFrame';
import type { PaperVerify } from '../domain/authorisation';

type Props = { result: PaperVerify | null; loading: boolean; unknown: boolean; failed: boolean; languageSwitcher: ReactNode };

/**
 * The QR check of an investigation slip or referral letter (MDV-P2-018/019), public: whoever holds the paper (a lab, a specialist) sees that it is
 * genuine, what it is, when and from whom, and how many items. Never the patient or what it lists. Genuine is an icon and words, never colour alone.
 */
export default function PaperVerifyPage(p: Props) {
  const { t, tCount, formatDate } = useI18n();
  const r = p.result;
  return (
    <AuthFrame languageSwitcher={p.languageSwitcher}>
      <section aria-labelledby="paper-verify-title" className="flex flex-col gap-3">
        <h1 id="paper-verify-title" className="text-xl font-extrabold">{t('paperVerify.title')}</h1>
        {p.loading && <p role="status">{t('paperVerify.loading')}</p>}
        {(p.unknown || p.failed) && <p role="alert" className="flex items-start gap-2 font-bold"><CircleAlert aria-hidden className="mt-0.5 size-5 shrink-0" />{t(p.unknown ? 'paperVerify.unknown' : 'paperVerify.failed')}</p>}
        {r && (
          <>
            <p role="status" className="flex items-start gap-2 text-lg font-bold"><ShieldCheck aria-hidden className="mt-0.5 size-5 shrink-0" />{t(`paperVerify.${r.kind}`)}</p>
            <p>{t('paperVerify.issued', { doctor: r.doctor, clinic: r.clinic, date: formatDate(new Date(r.issuedAt), r.timeZone) })}</p>
            <p>{tCount(`paperVerify.items.${r.kind}`, r.items)}</p>
          </>
        )}
        <p className="text-sm text-muted-foreground">{t('paperVerify.privacy')}</p>
      </section>
    </AuthFrame>
  );
}
