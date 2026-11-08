import { useRef } from 'react';
import { Link } from 'react-router';
import { Camera, CircleAlert, CircleCheck, Hourglass, ShieldAlert, Upload } from 'lucide-react';
import { ltr, useI18n } from '@medvya/i18n';
import { Button, buttonVariants, Input, Label, ProvenanceTag, Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@medvya/ui';
import type { ClinicalRefusal } from '../../patient360/domain/chart';
import { provenanceOf } from '../../patient360/domain/chart';
import { canOpenDocument, documentSources, documentTypes, sizeLabel, type DocumentList, type DocumentRow, type DocumentSource, type DocumentType, type ScanStatus, type UploadFailure, type ViewLink } from '../domain/documents';

type Props = {
  patient: DocumentList['patient'] | null; documents: DocumentRow[]; canOpen: boolean; timeZone: string;
  loading: boolean; notFound: boolean; refusal: ClinicalRefusal | null; failed: boolean;
  form: { type: DocumentType; date: string; source: DocumentSource }; onForm: (patch: { type?: DocumentType; date?: string; source?: DocumentSource }) => void;
  uploading: boolean; failure: UploadFailure | null; lastUpload: ScanStatus | null; canRetry: boolean;
  onFile: (file: File) => void; onRetry: () => void; onDismissFailure: () => void;
  pages: number; addingPhoto: boolean; photoFailed: boolean; onPhoto: (file: File) => void; onClearPages: () => void; onSavePages: () => void;
  viewer: ViewLink | null; expired: boolean; opening: string | null; openProblem: 'scan_pending' | 'quarantined' | 'failed' | null; onOpen: (id: string) => void; onCloseViewer: () => void;
  backPath: string;
};

/**
 * Documents (MDV-P2-006, E-03): upload a file or scan pages with the phone camera into one PDF, see each document's type, date, source and scan
 * state, and open a clean one in the watermarked viewer. The front desk uploads and sees the list but gets no Open button (the server refuses it too).
 */
export default function DocumentsPage(p: Props) {
  const { t } = useI18n();
  if (p.loading) return <p role="status">{t('documents.loading')}</p>;
  if (p.refusal) return <Message title={t('patient360.refused.title')} body={t(p.refusal.canRequest ? 'consult.refused.ask' : 'documents.refused.role')} backPath={p.backPath} />;
  if (p.notFound || p.failed || !p.patient) return <Message title={t(p.notFound ? 'patient360.notFound' : 'documents.loadFailed')} backPath={p.backPath} />;
  return (
    <div className="flex flex-col gap-4">
      <section aria-label={t('consult.banner.label')} className="flex flex-wrap items-baseline gap-x-4 gap-y-1 rounded-xl border-2 border-brand-700 bg-card p-4">
        <h1 className="text-xl font-extrabold">{p.patient.fullName}</h1>
        <span>{t('consult.banner.mrn', { mrn: ltr(p.patient.mrn) })}</span>
        <span>{t('documents.dob', { date: ltr(p.patient.dateOfBirth) })}</span>
        <Link to={p.backPath} className={buttonVariants({ variant: 'outline', className: 'ms-auto min-h-11' })}>{t('documents.back')}</Link>
      </section>
      <h2 className="text-lg font-extrabold">{t('documents.title')}</h2>
      <UploadPanel {...p} />
      {p.viewer && <Viewer viewer={p.viewer} timeZone={p.timeZone} onClose={p.onCloseViewer} />}
      {p.expired && <p role="status" className="font-bold">{t('documents.viewer.expired')}</p>}
      <section aria-label={t('documents.list')} className="flex flex-col gap-3 rounded-xl border bg-card p-4">
        {!p.canOpen && <p className="text-sm text-muted-foreground">{t('documents.deskNote')}</p>}
        {p.documents.length === 0 ? <p className="text-muted-foreground">{t('documents.empty')}</p>
          : <ul className="flex flex-col gap-3">{p.documents.map(d => <Row key={d.id} doc={d} {...p} />)}</ul>}
        {p.openProblem && <p role="alert" className="font-bold text-danger-700">{t(`documents.open.${p.openProblem}`)}</p>}
      </section>
    </div>
  );
}

function UploadPanel(p: Props) {
  const { t } = useI18n();
  const file = useRef<HTMLInputElement>(null), photo = useRef<HTMLInputElement>(null);
  const busy = p.uploading || p.addingPhoto;
  return (
    <section aria-label={t('documents.upload')} className="flex flex-col gap-3 rounded-xl border bg-card p-4">
      <h3 className="text-base font-extrabold">{t('documents.upload')}</h3>
      <div className="flex flex-wrap items-end gap-3">
        <div className="flex flex-col gap-1">
          <Label htmlFor="doc-type">{t('documents.field.type')}</Label>
          <Select value={p.form.type} onValueChange={v => p.onForm({ type: v as DocumentType })}>
            <SelectTrigger id="doc-type" className="h-11 w-52"><SelectValue /></SelectTrigger>
            <SelectContent>{documentTypes.map(x => <SelectItem key={x} value={x}>{t(`documents.type.${x}`)}</SelectItem>)}</SelectContent>
          </Select>
        </div>
        <div className="flex flex-col gap-1">
          <Label htmlFor="doc-date">{t('documents.field.date')}</Label>
          <Input id="doc-date" type="date" className="h-11 w-44" value={p.form.date} onChange={e => p.onForm({ date: e.target.value })} />
        </div>
        <div className="flex flex-col gap-1">
          <Label htmlFor="doc-source">{t('documents.field.source')}</Label>
          <Select value={p.form.source} onValueChange={v => p.onForm({ source: v as DocumentSource })}>
            <SelectTrigger id="doc-source" className="h-11 w-60"><SelectValue /></SelectTrigger>
            <SelectContent>{documentSources.map(x => <SelectItem key={x} value={x}>{t(`documents.source.${x}`)}</SelectItem>)}</SelectContent>
          </Select>
        </div>
      </div>
      <div className="flex flex-wrap items-center gap-3">
        <input ref={file} type="file" hidden accept="application/pdf,image/jpeg,image/png" aria-label={t('documents.chooseFile')}
          onChange={e => { const f = e.target.files?.[0]; e.target.value = ''; if (f) p.onFile(f); }} />
        <Button className="min-h-11" loading={p.uploading} disabled={busy} onClick={() => file.current?.click()}><Upload aria-hidden className="size-4" />{t('documents.chooseFile')}</Button>
        <input ref={photo} type="file" hidden accept="image/*" capture="environment" aria-label={t('documents.addPage')}
          onChange={e => { const f = e.target.files?.[0]; e.target.value = ''; if (f) p.onPhoto(f); }} />
        <Button variant="outline" className="min-h-11" loading={p.addingPhoto} disabled={busy} onClick={() => photo.current?.click()}><Camera aria-hidden className="size-4" />{t(p.pages === 0 ? 'documents.scan' : 'documents.addPage')}</Button>
      </div>
      {p.pages > 0 && (
        <div className="flex flex-wrap items-center gap-3 rounded-md border p-3">
          <span role="status" className="font-bold">{t('documents.pages', { count: p.pages })}</span>
          <Button className="min-h-11" loading={p.uploading} disabled={busy} onClick={p.onSavePages}>{t('documents.savePages', { count: p.pages })}</Button>
          <Button variant="ghost" className="min-h-11" disabled={busy} onClick={p.onClearPages}>{t('documents.discardPages')}</Button>
        </div>
      )}
      {p.photoFailed && <p role="alert" className="font-bold text-danger-700">{t('documents.photoFailed')}</p>}
      {p.uploading && <p role="status">{t('documents.uploading')}</p>}
      {p.lastUpload === 'Clean' && <p role="status" className="inline-flex items-center gap-1.5 font-bold"><CircleCheck aria-hidden className="size-4" />{t('documents.uploaded.clean')}</p>}
      {p.lastUpload === 'Pending' && <p role="status" className="inline-flex items-center gap-1.5 font-bold"><Hourglass aria-hidden className="size-4" />{t('documents.uploaded.pending')}</p>}
      {p.lastUpload === 'Infected' && <p role="alert" className="inline-flex items-center gap-1.5 font-bold text-danger-700"><ShieldAlert aria-hidden className="size-4" />{t('documents.uploaded.infected')}</p>}
      {p.failure && (
        <div role="alert" className="flex flex-wrap items-center gap-3 rounded-md border p-3">
          <span className="inline-flex items-center gap-1.5 font-bold text-danger-700"><CircleAlert aria-hidden className="size-4" />{t(`documents.failure.${p.failure}`)}</span>
          {p.canRetry && <Button className="min-h-11" onClick={p.onRetry}>{t('documents.retry')}</Button>}
          <Button variant="ghost" className="min-h-11" onClick={p.onDismissFailure}>{t('documents.dismiss')}</Button>
        </div>
      )}
    </section>
  );
}

function Row({ doc: d, ...p }: Props & { doc: DocumentRow }) {
  const { t, formatDate } = useI18n();
  const open = canOpenDocument(d, p.canOpen);
  return (
    <li className="flex flex-wrap items-start justify-between gap-x-4 gap-y-2 border-b pb-3 last:border-b-0">
      <div className="flex flex-col">
        <strong>{t(`documents.type.${d.type}`)}</strong>
        <span className="text-sm">{t('documents.documentDate', { date: formatDate(new Date(`${d.documentDate}T12:00:00Z`), 'UTC') })}</span>
        <span className="text-sm text-muted-foreground">{[d.uploadedBy, formatDate(new Date(d.uploadedAt), p.timeZone), sizeLabel(d.sizeBytes)].filter(Boolean).join(' · ')}</span>
      </div>
      <div className="flex flex-wrap items-center gap-2">
        <ProvenanceTag source={provenanceOf[d.source]} />
        <span className="inline-flex items-center gap-1.5 text-sm font-bold">
          {d.scanStatus === 'Clean' ? <CircleCheck aria-hidden className="size-4" /> : d.scanStatus === 'Pending' ? <Hourglass aria-hidden className="size-4" /> : <ShieldAlert aria-hidden className="size-4" />}
          {t(`documents.scan.${d.scanStatus}`)}
        </span>
        {open && <Button variant="navy" className="min-h-11" loading={p.opening === d.id} onClick={() => p.onOpen(d.id)} aria-label={t('documents.openFor', { type: t(`documents.type.${d.type}`) })}>{t('documents.open')}</Button>}
      </div>
    </li>
  );
}

const xml = (text: string) => text.replace(/[&<>"']/g, c => `&#${c.charCodeAt(0)};`);
/** A tile of the mark, rotated, as an SVG data image (img-src allows data:). */
const watermark = (text: string) =>
  `url("data:image/svg+xml,${encodeURIComponent(`<svg xmlns="http://www.w3.org/2000/svg" width="560" height="240"><text x="280" y="120" text-anchor="middle" transform="rotate(-25 280 120)" font-family="sans-serif" font-size="18" font-weight="700" fill="#102A43" fill-opacity="0.22">${xml(text)}</text></svg>`)}")`;

/** The secure viewer: the file from a link that expires, with who opened it and when written over it. The watermark is a deterrent on screen, not part of the file. */
function Viewer({ viewer, timeZone, onClose }: { viewer: ViewLink; timeZone: string; onClose: () => void }) {
  const { t, formatDate, formatTime } = useI18n();
  const at = new Date(viewer.viewedAt), until = new Date(viewer.expiresAt);
  const mark = `${viewer.viewer} · ${formatDate(at, timeZone)} ${formatTime(at, timeZone)}`;
  return (
    <section aria-label={t('documents.viewer')} className="flex flex-col gap-2 rounded-xl border-2 border-brand-700 bg-card p-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="text-sm">{t('documents.viewer.note', { time: formatTime(until, timeZone) })}</p>
        <Button variant="outline" className="min-h-11" onClick={onClose}>{t('documents.viewer.close')}</Button>
      </div>
      <div className="relative min-h-96 overflow-hidden rounded-md border bg-muted">
        {viewer.contentType === 'application/pdf'
          ? <iframe title={t('documents.viewer')} src={`${viewer.url}#toolbar=0&navpanes=0`} className="h-[70vh] w-full" referrerPolicy="no-referrer" />
          : <img src={viewer.url} alt={t('documents.viewer.alt')} className="mx-auto max-h-[70vh] object-contain" referrerPolicy="no-referrer" />}
        {/* A repeating background (not text), so it is a mark over the page and not content for readers or contrast checks. */}
        <div aria-hidden className="pointer-events-none absolute inset-0 select-none" style={{ backgroundImage: watermark(mark) }} />
      </div>
    </section>
  );
}

function Message({ title, body, backPath }: { title: string; body?: string; backPath: string }) {
  const { t } = useI18n();
  return (
    <div role="alert" className="flex flex-col items-start gap-3 rounded-xl border bg-card p-6">
      <h1 className="text-lg font-extrabold">{title}</h1>
      {body && <p>{body}</p>}
      <Link to={backPath} className={buttonVariants({ variant: 'outline', className: 'min-h-11' })}>{t('documents.back')}</Link>
    </div>
  );
}
