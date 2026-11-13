import { CalendarClock, Wallet } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Card } from '@medvya/ui';
import { specialtyIdOf, type Mode, type preview, type Rules } from '../domain/onboarding';

type Props = { view: ReturnType<typeof preview>; mode: Mode; currency: Rules['currency'] };

/** The live preview of the booking page (6 Nov design). Decorative mirror of the form: hidden from screen readers, which read the form itself. */
export default function BookingPreview({ view, mode, currency }: Props) {
  const { t, formatMoney } = useI18n();
  const days = view.firstDay && view.lastDay
    ? (view.firstDay === view.lastDay ? t(`onboarding.dayShort.${view.firstDay}`) : t('onboarding.preview.days', { from: t(`onboarding.dayShort.${view.firstDay}`), to: t(`onboarding.dayShort.${view.lastDay}`) }))
    : t('onboarding.closed');
  return (
    <div className="flex flex-col gap-3">
      <p className="text-xs font-bold text-muted-foreground">{t('onboarding.preview.label')}</p>
      <Card aria-hidden className="rounded-2xl p-5 shadow-sm">
        <span className="flex size-12 items-center justify-center rounded-xl bg-brand-700 text-lg font-extrabold text-white">{view.initials}</span>
        <strong className="text-lg">{view.name || t('onboarding.preview.unnamed')}</strong>
        <span className="text-sm text-muted-foreground">{[view.city, t(`onboarding.mode.${mode}`)].filter(Boolean).join(' · ')}</span>
        {view.specialties.length > 0 && (
          <div className="flex flex-wrap gap-1.5">
            {view.specialties.map(specialtyIdOf).filter(id => id !== undefined).map(id => (
              <span key={id} className="rounded-full bg-brand-50 px-2.5 py-1 text-xs font-semibold text-brand-800">{t(`onboarding.specialty.${id}`)}</span>
            ))}
          </div>
        )}
        <p className="flex items-center gap-2 text-sm"><CalendarClock className="size-4 shrink-0" /><span>{days}{view.hours && <bdi dir="ltr">{` · ${view.hours}`}</bdi>}</span></p>
        {view.fee !== null && <p className="flex items-center gap-2 text-sm"><Wallet className="size-4 shrink-0" />{t('onboarding.preview.fee', { fee: formatMoney(view.fee, currency), n: view.slotMinutes })}</p>}
        <div className="grid grid-cols-3 gap-1.5" dir="ltr">
          {view.slots.map((s, i) => (
            <span key={s} className={`rounded-md border px-2 py-1.5 text-center text-xs font-semibold ${i === 1 ? 'border-brand-700 bg-brand-50 text-brand-800' : ''}`}>{s}</span>
          ))}
        </div>
        <span className="rounded-lg bg-brand-700 py-2.5 text-center text-sm font-bold text-white">{t('onboarding.preview.book')}</span>
      </Card>
      <p className="text-xs text-muted-foreground">{t('onboarding.preview.note')}</p>
    </div>
  );
}
