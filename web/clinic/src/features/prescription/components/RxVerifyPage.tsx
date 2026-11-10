import type { ReactNode } from 'react';
import { CircleAlert, ShieldCheck } from 'lucide-react';
import { ltr, useI18n } from '@medvya/i18n';
import AuthFrame from '../../session/components/AuthFrame';
import type { RxVerify } from '../domain/authorisation';

type Props = { result: RxVerify | null; loading: boolean; unknown: boolean; failed: boolean; languageSwitcher: ReactNode };

/**
 * The QR verify page (MDV-P2-017), public: whoever holds the paper sees whether it is valid, its number and date, and who issued it. Never the
 * patient or the medicines. Validity is an icon and words, never colour alone.
 */
export default function RxVerifyPage(p: Props) {
  const { t, tCount, formatDate } = useI18n();
  const r = p.result;
  return (
    <AuthFrame languageSwitcher={p.languageSwitcher}>
      <section aria-labelledby="rx-verify-title" className="flex flex-col gap-3">
        <h1 id="rx-verify-title" className="text-xl font-extrabold">{t('rxVerify.title')}</h1>
        {p.loading && <p role="status">{t('rxVerify.loading')}</p>}
        {(p.unknown || p.failed) && <p role="alert" className="flex items-start gap-2 font-bold"><CircleAlert aria-hidden className="mt-0.5 size-5 shrink-0" />{t(p.unknown ? 'rxVerify.unknown' : 'rxVerify.failed')}</p>}
        {r && (
          <>
            <p role="status" className="flex items-start gap-2 text-lg font-bold">
              {r.status === 'Valid' ? <ShieldCheck aria-hidden className="mt-0.5 size-5 shrink-0" /> : <CircleAlert aria-hidden className="mt-0.5 size-5 shrink-0" />}
              {t(`rxVerify.${r.status}`)}
            </p>
            <p>{t('rxVerify.number', { number: ltr(r.number), version: r.versionNo })}</p>
            <p>{t('rxVerify.issued', { doctor: r.doctor, clinic: r.clinic, date: formatDate(new Date(r.authorisedAt), r.timeZone) })}</p>
            <p>{tCount('rxVerify.lines', r.lines)}</p>
          </>
        )}
        <p className="text-sm text-muted-foreground">{t('rxVerify.privacy')}</p>
      </section>
    </AuthFrame>
  );
}
