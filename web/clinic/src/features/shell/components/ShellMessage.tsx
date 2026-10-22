import { useI18n } from '@medvya/i18n';

/** One heading and one sentence: placeholder screens, empty navigation and loading. `standalone` adds the page landmark. */
export default function ShellMessage({ title, body, standalone = false, busy = false, heading: Heading = 'h1' }: { title: string; body?: string; standalone?: boolean; busy?: boolean; heading?: 'h1' | 'h2' }) {
  const content = (
    <div role={busy ? 'status' : undefined} className="flex max-w-[560px] flex-col gap-2 rounded-xl border bg-card p-6">
      <Heading className="text-xl font-extrabold">{title}</Heading>
      {body && <p className="text-muted-foreground">{body}</p>}
    </div>
  );
  return standalone ? <main aria-busy={busy || undefined} className="grid min-h-screen place-items-center p-4">{content}</main> : content;
}

/** Placeholder for a navigation target whose real screen lands in a later phase; never shows sample clinical data. */
export function ComingSoon({ title, phase }: { title: string; phase: number }) {
  const { t } = useI18n();
  return <ShellMessage title={title} body={t('shell.comingInPhase', { phase })} />;
}
