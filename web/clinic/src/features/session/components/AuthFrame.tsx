import type { ReactNode } from 'react';
import { useI18n, type Country } from '@medvya/i18n';
import { cn } from '@medvya/ui';
import { CircleCheck, CircleDot, Circle, Globe, LayoutDashboard, ShieldCheck, UserCheck } from 'lucide-react';

export type FrameStep = { label: string; state: 'done' | 'current' | 'todo' };

type Props = {
  languageSwitcher: ReactNode; children: ReactNode; steps?: FrameStep[];
  /**
   * The data region line, only where the country is known (create account: the one chosen).
   * shortcut: signed out, the app does not know its deployment's country, so sign-in shows no region line; add it when the server exposes the country.
   */
  country?: Country;
};

const points = [
  { icon: LayoutDashboard, key: 'signin.point.overview' },
  { icon: UserCheck, key: 'signin.point.consent' },
  { icon: Globe, key: 'signin.point.residency' },
] as const;
const stepIcons = { done: CircleCheck, current: CircleDot, todo: Circle };

function Mark({ light }: { light?: boolean }) {
  const { t } = useI18n();
  return (
    <span className="flex items-center gap-2">
      <span aria-hidden className="grid size-8 place-items-center rounded-[9px] bg-brand-700 font-extrabold text-white">{t('app.name').charAt(0)}</span>
      <span className={cn('text-lg font-extrabold', light ? 'text-white' : 'text-brand-950')}>{t('app.name')}</span>
    </span>
  );
}

/**
 * The 6 Nov sign-in frame (MDV-P1-059): navy brand panel from 768 px, the form column with the language switcher and the data-region
 * line. Shared by sign-in, create account, invitation, choose location and signed out. The steps list shows only while creating an account.
 */
export default function AuthFrame({ languageSwitcher, children, steps, country }: Props) {
  const { t } = useI18n();
  const current = steps ? steps.findIndex(s => s.state === 'current') + 1 : 0;
  return (
    <div className="flex min-h-screen">
      <aside aria-label={t('app.name')} className="hidden w-[42%] shrink-0 flex-col gap-6 bg-brand-950 px-12 py-10 text-white md:flex [&_:focus-visible]:outline-white">
        <Mark light />
        <div className="mt-auto flex max-w-[440px] flex-col gap-[18px]">
          <p className="text-[28px] leading-tight font-bold">{t('signin.tagline')}</p>
          <ul className="flex flex-col gap-3">
            {points.map(({ icon: Icon, key }) => (
              <li key={key} className="flex items-start gap-3 text-[15px] text-nav-ink">
                <Icon className="mt-0.5 size-5 shrink-0 text-brand-500" aria-hidden />{t(key)}
              </li>
            ))}
          </ul>
        </div>
        {steps && (
          <ol aria-label={t('signin.create.steps')} className="flex flex-col gap-2 rounded-xl bg-nav-hover p-4">
            {steps.map(s => {
              const Icon = stepIcons[s.state];
              return (
                <li key={s.label} aria-current={s.state === 'current' ? 'step' : undefined}
                  className={cn('flex items-center gap-2 text-sm', s.state === 'current' ? 'font-bold text-white' : 'text-nav-ink')}>
                  <Icon className="size-5 shrink-0 text-brand-500" aria-hidden />{s.label}
                </li>
              );
            })}
          </ol>
        )}
        <p className="mt-auto flex items-center gap-2 text-[13px] text-nav-muted"><ShieldCheck className="size-4 shrink-0" aria-hidden />{t('signin.trust')}</p>
      </aside>
      <div className="flex min-w-0 flex-1 flex-col bg-card">
        <header className="flex items-center justify-between gap-3 px-4 py-3 md:px-8 md:py-5">
          <span className="md:hidden"><Mark /></span>
          <span className="max-md:hidden" />
          {languageSwitcher}
        </header>
        <main className="flex flex-1 flex-col">
          {steps && (
            <div className="flex items-center gap-3 px-4 pb-2 text-[13px] font-bold text-muted-foreground md:hidden">
              <span>{t('signin.create.stepOf', { n: current, total: steps.length })}</span>
              <span aria-hidden className="h-1.5 flex-1 overflow-hidden rounded-full bg-muted">
                <span className="block h-full bg-brand-700" style={{ width: `${(current / steps.length) * 100}%` }} />
              </span>
            </div>
          )}
          <div className="flex flex-1 items-center justify-center px-4 py-6 md:px-8">
            <div className="flex w-full max-w-[420px] flex-col gap-[18px]">{children}</div>
          </div>
        </main>
        {country && (
          <footer className="flex flex-wrap items-center gap-1.5 px-4 py-4 text-[13px] text-muted-foreground md:px-8">
            <Globe className="size-4 shrink-0" aria-hidden />{t('signin.region')}
            <strong className="text-foreground">{t(`signin.region.${country}`)}</strong>
          </footer>
        )}
      </div>
    </div>
  );
}
