import { CircleCheck, TriangleAlert } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Card, Button, Dialog, DialogContent, DialogDescription, DialogTitle, StickyActionBar } from '@medvya/ui';
import type { TermsStatus } from '../domain/settings';
import { cardBar, fieldError } from './fieldStyles';

type Props = {
  terms: TermsStatus | null; loading: boolean; loadFailed: boolean; accepting: boolean; notOwner: boolean; acceptFailed: boolean;
  /** The confirmation is open (#1365 F-038): the button asks, Confirm in the dialog accepts. */
  confirming: boolean; onAsk: () => void; onCancel: () => void; onAccept: () => void;
};

/** Terms of Service and DPA (MDV-P1-019): the owner accepts once; while acceptance is required and missing, booking and messages are refused by the server. */
export default function TermsPanel(p: Props) {
  const { t, formatDate } = useI18n();
  if (p.loading) return <p role="status">{t('settings.loading')}</p>;
  if (p.loadFailed || !p.terms) return <p role="alert" className={fieldError}>{t('settings.loadFailed')}</p>;
  const { terms } = p;
  return (
    <Card title={t('settings.terms.title')} intro={t('settings.terms.intro')}>
      <p className="rounded-md border border-warning-700 px-3 py-2 text-sm font-semibold">
        {t('settings.terms.draft')} <bdi dir="ltr">{t('settings.terms.version', { version: terms.version })}</bdi>
      </p>
      <div className="flex flex-col gap-1">
        <h3 className="font-bold">{t('settings.terms.licenceTitle')}</h3>
        <p>{t('settings.terms.licenceClause')}</p>
      </div>
      {/* shortcut: the full Terms of Service and DPA text is not published anywhere in the product yet (MDV-P1-044, counsel's review), so
          there is nothing to link to; the page says so instead of implying it was read. Link the published documents here once they exist. */}
      <p className="text-sm text-muted-foreground">{t('settings.terms.noText')}</p>
      {terms.accepted && terms.acceptedAt ? (
        <p role="status" className="flex items-center gap-2 font-bold"><CircleCheck aria-hidden className="size-5" />{t('settings.terms.accepted', { date: formatDate(new Date(terms.acceptedAt)) })}</p>
      ) : (
        <>
          {/* The server's own answer: the gate refuses booking only while acceptance is required (TermsAcceptances.Required). */}
          {terms.required
            ? <p className="flex items-center gap-2 font-bold"><TriangleAlert aria-hidden className="size-5 text-warning-700" />{t('settings.terms.pending')}</p>
            : <p className="font-bold">{t('settings.terms.notRequired')}</p>}
          <StickyActionBar className={cardBar} status={<>
            {p.notOwner && <p role="alert" className={fieldError}>{t('settings.terms.notOwner')}</p>}
            {p.acceptFailed && <p role="alert" className={fieldError}>{t('settings.terms.failed')}</p>}
          </>}>
            <Button onClick={p.onAsk}>{t('settings.terms.accept')}</Button>
          </StickyActionBar>
          <Dialog open={p.confirming} onOpenChange={open => { if (!open && !p.accepting) p.onCancel(); }}>
            <DialogContent closeLabel={t('settings.terms.confirm.cancel')}>
              <DialogTitle>{t('settings.terms.confirm.title')}</DialogTitle>
              <DialogDescription>{t('settings.terms.confirm.body')} <bdi dir="ltr">{t('settings.terms.version', { version: terms.version })}</bdi></DialogDescription>
              <div className="flex flex-wrap gap-3">
                <Button loading={p.accepting} onClick={p.onAccept}>{t('settings.terms.confirm.confirm')}</Button>
                <Button variant="outline" disabled={p.accepting} onClick={p.onCancel}>{t('settings.terms.confirm.cancel')}</Button>
              </div>
            </DialogContent>
          </Dialog>
        </>
      )}
    </Card>
  );
}
