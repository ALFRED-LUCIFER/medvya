import { CircleCheck, TriangleAlert } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button } from '@medvya/ui';
import type { TermsStatus } from '../domain/settings';
import { fieldError } from './fieldStyles';
import { Section } from './parts';

type Props = { terms: TermsStatus | null; loading: boolean; loadFailed: boolean; accepting: boolean; notOwner: boolean; acceptFailed: boolean; onAccept: () => void };

/** Terms of Service and DPA (MDV-P1-019): the owner accepts once; until then booking and messages are refused by the server. */
export default function TermsPanel(p: Props) {
  const { t, formatDate } = useI18n();
  if (p.loading) return <p role="status">{t('settings.loading')}</p>;
  if (p.loadFailed || !p.terms) return <p role="alert" className={fieldError}>{t('settings.loadFailed')}</p>;
  const { terms } = p;
  return (
    <Section title={t('settings.terms.title')} intro={t('settings.terms.intro')}>
      <p className="rounded-md border border-warning-700 px-3 py-2 text-sm font-semibold">
        {t('settings.terms.draft')} <bdi dir="ltr">{t('settings.terms.version', { version: terms.version })}</bdi>
      </p>
      {terms.accepted && terms.acceptedAt ? (
        <p role="status" className="flex items-center gap-2 font-bold"><CircleCheck aria-hidden className="size-5" />{t('settings.terms.accepted', { date: formatDate(new Date(terms.acceptedAt)) })}</p>
      ) : (
        <>
          <p className="flex items-center gap-2 font-bold"><TriangleAlert aria-hidden className="size-5 text-warning-700" />{t('settings.terms.pending')}</p>
          <Button className="self-start" loading={p.accepting} onClick={p.onAccept}>{t('settings.terms.accept')}</Button>
          {p.notOwner && <p role="alert" className={fieldError}>{t('settings.terms.notOwner')}</p>}
          {p.acceptFailed && <p role="alert" className={fieldError}>{t('settings.terms.failed')}</p>}
        </>
      )}
    </Section>
  );
}
