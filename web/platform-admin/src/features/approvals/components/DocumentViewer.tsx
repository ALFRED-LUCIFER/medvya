import { useI18n } from '@medvya/i18n';
import { Button, Card } from '@medvya/ui';
import type { DocumentView } from '../domain/approvals';

/** The watermark as a tiled picture (not text in the page), so it marks the file without being content a reader or a contrast check has to read. */
const watermark = (text: string) => {
  const escaped = text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="360" height="160"><text x="180" y="80" text-anchor="middle" transform="rotate(-18 180 80)" font-family="sans-serif" font-size="15" font-weight="700" fill="#6b7280" fill-opacity="0.45">${escaped}</text></svg>`;
  return `url("data:image/svg+xml,${encodeURIComponent(svg)}")`;
};

/**
 * The private viewer: the file is shown on this page from memory, with no download button, no right-click menu and the operator's name and time
 * written over it. Every view is logged on the server. shortcut: the watermark is drawn over the file, not burned into it (see the server note).
 */
export default function DocumentViewer({ file, operator, onClose }: { file: { doc: DocumentView; url: string; contentType: string; openedAt: Date }; operator: string; onClose: () => void }) {
  const { t, formatDate, formatTime } = useI18n();
  const mark = t('platform.approvals.viewer.watermark', { name: operator, when: `${formatDate(file.openedAt)} ${formatTime(file.openedAt)}` });
  const isPdf = file.contentType === 'application/pdf';
  return (
    <Card size="item" aria-label={t('platform.approvals.viewer.title')} onContextMenu={e => e.preventDefault()}>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-sm text-muted-foreground">{t('platform.approvals.viewer.note')}</p>
        <Button variant="outline" onClick={onClose}>{t('platform.approvals.viewer.close')}</Button>
      </div>
      <div className="relative min-h-64 overflow-hidden rounded-md bg-muted select-none">
        {isPdf
          ? <iframe src={`${file.url}#toolbar=0&navpanes=0`} title={t('platform.approvals.viewer.title')} className="h-[32rem] w-full" />
          : <img src={file.url} alt={t('platform.approvals.viewer.title')} draggable={false} className="mx-auto max-h-[32rem] object-contain" />}
        <div aria-hidden className="pointer-events-none absolute inset-0" style={{ backgroundImage: watermark(mark) }} />
      </div>
    </Card>
  );
}
