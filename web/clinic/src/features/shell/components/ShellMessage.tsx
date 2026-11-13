import { useI18n } from '@medvya/i18n';
import { PageContainer, PageHeader } from '@medvya/ui';

/** One heading and one sentence: placeholder screens, empty navigation and loading. The h1 is the page's PageHeader; `standalone` adds the page landmark. */
export default function ShellMessage({ title, body, standalone = false, busy = false, heading = 'h1' }: { title: string; body?: string; standalone?: boolean; busy?: boolean; heading?: 'h1' | 'h2' }) {
  const content = (
    <PageContainer width="form" role={busy ? 'status' : undefined}>
      {heading === 'h1' ? <PageHeader title={title} description={body} /> : (
        <div className="flex flex-col gap-1">
          <h2 className="text-heading">{title}</h2>
          {body && <p className="text-muted-foreground">{body}</p>}
        </div>
      )}
    </PageContainer>
  );
  return standalone ? <main aria-busy={busy || undefined} className="grid min-h-screen place-items-center p-4">{content}</main> : content;
}

/** Placeholder for a navigation target whose real screen lands in a later phase; never shows sample clinical data. */
export function ComingSoon({ title, phase }: { title: string; phase: number }) {
  const { t } = useI18n();
  return <ShellMessage title={title} body={t('shell.comingInPhase', { phase })} />;
}
