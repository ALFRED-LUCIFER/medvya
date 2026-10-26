import type { ReactNode } from 'react';
import { Bell, BellOff, MonitorX, Radio, WifiOff } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import type { LiveStatus } from '../../front-desk/domain/board';
import type { QueueTv, Unpaired } from '../domain/queueTv';

type Props = {
  tv: QueueTv | null;
  pairing: boolean;
  loading: boolean;
  unpaired: Unpaired | null;
  failed: boolean;
  live: LiveStatus;
  chime: boolean;
  onToggleChime: () => void;
  languageSwitcher: ReactNode;
};

// Tokens read left to right in every language ("A-07"), also inside Arabic.
const Token = ({ value, className }: { value: string; className: string }) => <bdi dir="ltr" className={`font-mono font-semibold ${className}`}>{value}</bdi>;

/**
 * P-05 waiting-room TV (Clinical Screens): "Now calling" with token, room and doctor, and "Next" with token and wait. Token
 * numbers only, never names. Large type for 3 metres (tokens at least 48 px), light on deep navy, nothing animates.
 */
export default function QueueTvPage(props: Props) {
  const { t } = useI18n();
  const { tv } = props;
  return (
    <main className="flex min-h-dvh flex-col gap-6 bg-tv-bg p-6 text-white lg:p-10">
      <h1 className="sr-only">{t('queueTv.title')}</h1>
      {props.unpaired ? <Notice icon={<MonitorX aria-hidden className="size-14" />} title={t(`queueTv.unpaired.${props.unpaired}.title`)} body={t(`queueTv.unpaired.${props.unpaired}.body`)} />
        : props.pairing ? <Notice busy title={t('queueTv.pairing')} />
        : props.failed ? <Notice title={t('queueTv.error.title')} body={t('queueTv.error.body')} />
        : props.loading || !tv ? <Notice busy title={t('queueTv.loading')} />
        : (
          <div className="grid flex-1 gap-8 lg:grid-cols-[1.2fr_1fr]">
            <section aria-labelledby="tv-now" className="flex flex-col gap-4">
              <h2 id="tv-now" className="text-[28px] font-bold text-tv-heading">{t('queueTv.now')}</h2>
              {tv.now.length === 0 ? <p className="text-[28px] text-tv-muted">{t('queueTv.empty.now')}</p> : (
                <ul className="flex flex-col gap-4">
                  {tv.now.map(n => (
                    <li key={n.token} className="flex flex-wrap items-center gap-x-8 gap-y-2 rounded-2xl bg-tv-panel px-6 py-5">
                      <Token value={n.token} className="text-[80px] leading-none" />
                      <span className="flex flex-col">
                        {n.room && <span className="text-[36px] font-extrabold">{n.room}</span>}
                        <span className="text-[26px] text-tv-muted">{n.doctor}</span>
                      </span>
                    </li>
                  ))}
                </ul>
              )}
            </section>
            <section aria-labelledby="tv-next" className="flex flex-col gap-3">
              <h2 id="tv-next" className="text-[28px] font-bold text-tv-muted">{t('queueTv.next')}</h2>
              {tv.next.length === 0 ? <p className="text-[28px] text-tv-muted">{t('queueTv.empty.next')}</p> : (
                <ul className="flex flex-col gap-3">
                  {tv.next.map(n => (
                    <li key={n.token} className="flex items-center justify-between gap-6 rounded-xl bg-brand-950 px-6 py-3">
                      <Token value={n.token} className="text-[48px] leading-tight" />
                      <span className="text-[30px] text-tv-muted">{t('queueTv.wait', { minutes: n.waitMinutes })}</span>
                    </li>
                  ))}
                </ul>
              )}
            </section>
          </div>
        )}
      <footer className="flex flex-wrap items-center justify-between gap-4 border-t border-tv-panel pt-4">
        <p className="text-[22px] text-tv-note">{t('queueTv.emergency')}</p>
        <div className="flex flex-wrap items-center gap-4">
          {/* Words and an icon, not colour alone; announced when it changes. */}
          <p role="status" className="flex items-center gap-2 text-lg font-bold text-tv-muted">
            {props.live === 'live' ? <Radio aria-hidden className="size-5" /> : <WifiOff aria-hidden className="size-5" />}
            {t(`board.live.${props.live}`)}
          </p>
          <button type="button" aria-pressed={props.chime} onClick={props.onToggleChime}
            className="flex min-h-12 items-center gap-2 rounded-lg border border-tv-muted px-4 text-lg font-bold text-white focus-visible:outline-3 focus-visible:outline-offset-2 focus-visible:outline-white">
            {props.chime ? <Bell aria-hidden className="size-5" /> : <BellOff aria-hidden className="size-5" />}
            {t(props.chime ? 'queueTv.chime.on' : 'queueTv.chime.off')}
          </button>
          <div className="rounded-lg bg-card p-2 text-foreground">{props.languageSwitcher}</div>
        </div>
      </footer>
    </main>
  );
}

function Notice({ title, body, icon, busy = false }: { title: string; body?: string; icon?: ReactNode; busy?: boolean }) {
  return (
    <div role={busy ? 'status' : 'alert'} className="m-auto flex max-w-[900px] flex-col items-center gap-4 text-center">
      {icon}
      <p className="text-[40px] font-extrabold">{title}</p>
      {body && <p className="text-[26px] text-tv-muted">{body}</p>}
    </div>
  );
}
