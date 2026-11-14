import { useRef } from 'react';
import { Camera, CircleAlert, CircleCheck, Hourglass, Pencil, ShieldAlert, Upload } from 'lucide-react';
import { ltr, useI18n } from '@medvya/i18n';
import { Button, Card, Input, Label, PageContainer, PageHeader, PageMessage, ProvenanceTag, Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@medvya/ui';
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
  editing: { id: string; type: DocumentType; date: string } | null; saving: boolean; editFailed: boolean;
  onEdit: (d: DocumentRow) => void; onEditChange: (patch: { type?: DocumentType; date?: string }) => void; onSaveEdit: () => void; onCancelEdit: () => void;
};

/**
 * Documents (MDV-P2-006, E-03): upload a file or scan pages with the phone camera into one PDF, see each document's type, date, source and scan
 * state, and open a clean one in the watermarked viewer. The front desk uploads and sees the list but gets no Open button (the server refuses it too).
 */
export default function DocumentsPage(p: Props) {
  const { t, formatCalendarDate } = useI18n();
  // One header in every state, so the title never moves (MDV-P2-059); the record's tabs above it are the way back (MDV-P2-051).
  const header = <PageHeader title={t('documents.title')} />;
  if (p.loading) return <PageContainer>{header}<PageMessage kind="busy">{t('documents.loading')}</PageMessage></PageContainer>;
  if (p.refusal)
    return <PageContainer>{header}<PageMessage kind="denied" title={t('patient360.refused.title')}>{t(p.refusal.canRequest ? 'consult.refused.ask' : 'documents.refused.role')}</PageMessage></PageContainer>;
  if (p.notFound || p.failed || !p.patient) return <PageContainer>{header}<PageMessage kind="error">{t(p.notFound ? 'patient360.notFound' : 'documents.loadFailed')}</PageMessage></PageContainer>;
  return (
    <PageContainer>
      {header}
      <section aria-label={t('consult.banner.label')} className="flex flex-wrap items-baseline gap-x-4 gap-y-1 rounded-xl border-2 border-brand-700 bg-card p-4">
        <h2 className="text-heading">{p.patient.fullName}</h2>
        <span>{t('consult.banner.mrn', { mrn: ltr(p.patient.mrn) })}</span>
        <span>{t('documents.dob', { date: formatCalendarDate(p.patient.dateOfBirth) })}</span>
      </section>
      <UploadPanel {...p} />
      {p.viewer && <Viewer viewer={p.viewer} timeZone={p.timeZone} onClose={p.onCloseViewer} />}
      {p.expired && <p role="status" className="font-bold">{t('documents.viewer.expired')}</p>}
      <Card aria-label={t('documents.list')}>
        {!p.canOpen && <p className="text-sm text-muted-foreground">{t('documents.deskNote')}</p>}
        {p.documents.length === 0 ? <p className="text-muted-foreground">{t('documents.empty')}</p>
          : <ul className="flex flex-col gap-3">{p.documents.map(d => <Row key={d.id} doc={d} {...p} />)}</ul>}
        {p.openProblem && <p role="alert" className="font-bold text-danger-700">{t(`documents.open.${p.openProblem}`)}</p>}
      </Card>
    </PageContainer>
  );
}

function UploadPanel(p: Props) {
  const { t } = useI18n();
  const file = useRef<HTMLInputElement>(null), photo = useRef<HTMLInputElement>(null);
  const busy = p.uploading || p.addingPhoto;
  return (
    <Card title={t('documents.upload')}>
      <div className="flex flex-wrap items-end gap-3">
        <div className="flex flex-col gap-1">
          <Label htmlFor="doc-type">{t('documents.field.type')}</Label>
          <Select value={p.form.type} onValueChange={v => p.onForm({ type: v as DocumentType })}>
            <SelectTrigger id="doc-type" className="w-52"><SelectValue /></SelectTrigger>
            <SelectContent>{documentTypes.map(x => <SelectItem key={x} value={x}>{t(`documents.type.${x}`)}</SelectItem>)}</SelectContent>
          </Select>
        </div>
        <div className="flex flex-col gap-1">
          <Label htmlFor="doc-date">{t('documents.field.date')}</Label>
          <Input id="doc-date" type="date" className="w-44" value={p.form.date} onChange={e => p.onForm({ date: e.target.value })} />
        </div>
        <div className="flex flex-col gap-1">
          <Label htmlFor="doc-source">{t('documents.field.source')}</Label>
          <Select value={p.form.source} onValueChange={v => p.onForm({ source: v as DocumentSource })}>
            <SelectTrigger id="doc-source" className="w-60"><SelectValue /></SelectTrigger>
            <SelectContent>{documentSources.map(x => <SelectItem key={x} value={x}>{t(`documents.source.${x}`)}</SelectItem>)}</SelectContent>
          </Select>
        </div>
      </div>
      <div className="flex flex-wrap items-center gap-3">
        <input ref={file} type="file" hidden accept="application/pdf,image/jpeg,image/png" aria-label={t('documents.chooseFile')}
          onChange={e => { const f = e.target.files?.[0]; e.target.value = ''; if (f) p.onFile(f); }} />
        <Button loading={p.uploading} disabled={busy} onClick={() => file.current?.click()}><Upload aria-hidden className="size-4" />{t('documents.chooseFile')}</Button>
        <input ref={photo} type="file" hidden accept="image/*" capture="environment" aria-label={t('documents.addPage')}
          onChange={e => { const f = e.target.files?.[0]; e.target.value = ''; if (f) p.onPhoto(f); }} />
        <Button variant="outline" loading={p.addingPhoto} disabled={busy} onClick={() => photo.current?.click()}><Camera aria-hidden className="size-4" />{t(p.pages === 0 ? 'documents.scan' : 'documents.addPage')}</Button>
      </div>
      {p.pages > 0 && (
        <div className="flex flex-wrap items-center gap-3 rounded-md border p-3">
          <span role="status" className="font-bold">{t('documents.pages', { count: p.pages })}</span>
          <Button loading={p.uploading} disabled={busy} onClick={p.onSavePages}>{t('documents.savePages', { count: p.pages })}</Button>
          <Button variant="ghost" disabled={busy} onClick={p.onClearPages}>{t('documents.discardPages')}</Button>
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
          {p.canRetry && <Button onClick={p.onRetry}>{t('documents.retry')}</Button>}
          <Button variant="ghost" onClick={p.onDismissFailure}>{t('documents.dismiss')}</Button>
        </div>
      )}
    </Card>
  );
}

function Row({ doc: d, ...p }: Props & { doc: DocumentRow }) {
  const { t, formatDate, formatCalendarDate } = useI18n();
  const open = canOpenDocument(d, p.canOpen);
  const editing = p.editing?.id === d.id ? p.editing : null;
  return (
    <li className="flex flex-wrap items-start justify-between gap-x-4 gap-y-2 border-b pb-3 last:border-b-0">
      <div className="flex flex-col">
        <strong>{t(`documents.type.${d.type}`)}</strong>
        <span className="text-sm">{t('documents.documentDate', { date: formatCalendarDate(d.documentDate) })}</span>
        <span className="text-sm text-muted-foreground">{[d.uploadedBy, formatDate(new Date(d.uploadedAt), p.timeZone), sizeLabel(d.sizeBytes)].filter(Boolean).join(' · ')}</span>
      </div>
      <div className="flex flex-wrap items-center gap-2">
        <ProvenanceTag source={provenanceOf[d.source]} />
        <span className="inline-flex items-center gap-1.5 text-sm font-bold">
          {d.scanStatus === 'Clean' ? <CircleCheck aria-hidden className="size-4" /> : d.scanStatus === 'Pending' ? <Hourglass aria-hidden className="size-4" /> : <ShieldAlert aria-hidden className="size-4" />}
          {t(`documents.scan.${d.scanStatus}`)}
        </span>
        {p.canOpen && d.scanStatus !== 'Infected' && !editing && <Button variant="outline" onClick={() => p.onEdit(d)} aria-label={t('documents.editFor', { type: t(`documents.type.${d.type}`) })}><Pencil aria-hidden className="size-4" />{t('documents.edit')}</Button>}
        {open && <Button variant="default" loading={p.opening === d.id} onClick={() => p.onOpen(d.id)} aria-label={t('documents.openFor', { type: t(`documents.type.${d.type}`) })}>{t('documents.open')}</Button>}
      </div>
      {editing && (
        <div role="group" aria-label={t('documents.editing')} className="flex w-full flex-wrap items-end gap-3 rounded-md border p-3">
          <div className="flex flex-col gap-1">
            <Label htmlFor={`edit-type-${d.id}`}>{t('documents.field.type')}</Label>
            <Select value={editing.type} onValueChange={v => p.onEditChange({ type: v as DocumentType })}>
              <SelectTrigger id={`edit-type-${d.id}`} className="w-52"><SelectValue /></SelectTrigger>
              <SelectContent>{documentTypes.map(x => <SelectItem key={x} value={x}>{t(`documents.type.${x}`)}</SelectItem>)}</SelectContent>
            </Select>
          </div>
          <div className="flex flex-col gap-1">
            <Label htmlFor={`edit-date-${d.id}`}>{t('documents.field.date')}</Label>
            <Input id={`edit-date-${d.id}`} type="date" className="w-44" value={editing.date} onChange={e => p.onEditChange({ date: e.target.value })} />
          </div>
          <Button loading={p.saving} onClick={p.onSaveEdit}>{t('documents.edit.save')}</Button>
          <Button variant="ghost" disabled={p.saving} onClick={p.onCancelEdit}>{t('documents.edit.cancel')}</Button>
          <p className="basis-full text-sm text-muted-foreground">{t('documents.edit.note')}</p>
          {p.editFailed && <p role="alert" className="basis-full font-bold text-danger-700">{t('documents.edit.failed')}</p>}
        </div>
      )}
    </li>
  );
}

const xml = (text: string) => text.replace(/[&<>"']/g, c => `&#${c.charCodeAt(0)};`);
/** A tile of the mark, rotated, as an SVG data image (img-src allows data:). */
const watermark = (text: string) =>
  `url("data:image/svg+xml,${encodeURIComponent(`<svg xmlns="http://www.w3.org/2000/svg" width="560" height="240"><text x="280" y="120" text-anchor="middle" transform="rotate(-25 280 120)" font-family="sans-serif" font-size="18" font-weight="700" fill="#102A43" fill-opacity="0.22">${xml(text)}</text></svg>`)}")`;

/** The secure viewer: the file from a link that expires, with who opened it and when written over it. shortcut: the watermark is drawn over the file on screen, not burned into it (decided 11 Nov, #955, as for the console in ADR-014): a screenshot is marked but the served bytes are not; bake it in only if downloads are ever allowed. */
function Viewer({ viewer, timeZone, onClose }: { viewer: ViewLink; timeZone: string; onClose: () => void }) {
  const { t, formatDate, formatTime } = useI18n();
  const at = new Date(viewer.viewedAt), until = new Date(viewer.expiresAt);
  const mark = `${viewer.viewer} · ${formatDate(at, timeZone)} ${formatTime(at, timeZone)}`;
  return (
    <section aria-label={t('documents.viewer')} className="flex flex-col gap-2 rounded-xl border-2 border-brand-700 bg-card p-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="text-sm">{t('documents.viewer.note', { time: formatTime(until, timeZone) })}</p>
        <Button variant="outline" onClick={onClose}>{t('documents.viewer.close')}</Button>
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
