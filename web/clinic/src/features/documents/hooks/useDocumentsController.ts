import { useEffect, useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ApiError } from '../../schedule/services/scheduleApi';
import { refusalOf } from '../../patient360/domain/chart';
import { anyPending, fileProblem, todayIn, uploadFailure, type DocumentSource, type DocumentType, type ScanStatus, type UploadFailure, type ViewLink } from '../domain/documents';
import { buildPdf, type PageImage } from '../domain/pdf';
import { fetchDocuments, openDocument, updateDocument, uploadDocument, type UploadDetails } from '../services/documentsApi';
import { toPageImage } from '../services/imagePages';

type Upload = { file: Blob; pages: boolean };

/**
 * One patient's documents (MDV-P2-006): the list (refreshed while a scan is running), uploading a file or several camera pages as one PDF with
 * a retry that keeps the file, and the secure viewer. A view link lives five minutes at most, so the viewer closes itself when it expires.
 */
export function useDocumentsController(patientId: string) {
  const queryClient = useQueryClient();
  const key = ['documents', patientId];
  const list = useQuery({
    queryKey: key, queryFn: () => fetchDocuments(patientId), retry: false, refetchOnWindowFocus: false,
    refetchInterval: q => (q.state.data && anyPending(q.state.data.documents) ? 3000 : false),
  });
  const zone = list.data?.timeZone ?? 'UTC';
  const [form, setForm] = useState<{ type: DocumentType; date: string | null; source: DocumentSource }>({ type: 'LabReport', date: null, source: 'Imported' });
  const details: UploadDetails = { type: form.type, date: form.date ?? todayIn(zone), source: form.source };

  const [failure, setFailure] = useState<UploadFailure | null>(null);
  const [retry, setRetry] = useState<Upload | null>(null);
  const [lastUpload, setLastUpload] = useState<ScanStatus | null>(null);
  const [pages, setPages] = useState<PageImage[]>([]);
  const send = useMutation({
    mutationFn: ({ file }: Upload) => uploadDocument(patientId, details, file),
    onSuccess: (row, upload) => {
      setFailure(null); setRetry(null); setLastUpload(row.scanStatus);
      if (upload.pages) setPages([]); // the camera pages are in the record now
      void queryClient.invalidateQueries({ queryKey: key });
    },
    // The file is kept so "try again" sends the same one.
    onError: (e, upload) => { setRetry(upload); setFailure(e instanceof ApiError ? uploadFailure(e.status, e.code) : 'failed'); },
  });
  const submit = (upload: Upload) => { setLastUpload(null); send.mutate(upload); };

  const [photoFailed, setPhotoFailed] = useState(false);
  const addPhoto = useMutation({
    mutationFn: toPageImage,
    onSuccess: page => { setPhotoFailed(false); setPages(p => [...p, page]); },
    onError: () => setPhotoFailed(true),
  });

  const [viewer, setViewer] = useState<ViewLink | null>(null);
  const [expired, setExpired] = useState(false);
  const open = useMutation({ mutationFn: openDocument, onSuccess: link => { setExpired(false); setViewer(link); } });
  const closeAt = useRef<ReturnType<typeof setTimeout>>(undefined);
  useEffect(() => {
    clearTimeout(closeAt.current);
    if (viewer) closeAt.current = setTimeout(() => { setViewer(null); setExpired(true); }, Math.max(0, Date.parse(viewer.expiresAt) - Date.now()));
    return () => clearTimeout(closeAt.current);
  }, [viewer]);

  const [editing, setEditing] = useState<{ id: string; type: DocumentType; date: string } | null>(null);
  const edit = useMutation({
    mutationFn: (e: { id: string; type: DocumentType; date: string }) => updateDocument(e.id, { type: e.type, documentDate: e.date }),
    onSuccess: () => { setEditing(null); void queryClient.invalidateQueries({ queryKey: key }); },
  });

  const error = list.error instanceof ApiError ? list.error : null;
  const openError = open.error instanceof ApiError ? open.error : null;
  return {
    patient: list.data?.patient ?? null,
    documents: list.data?.documents ?? [],
    canOpen: list.data?.canOpen ?? false,
    timeZone: zone,
    loading: list.isPending,
    notFound: error?.status === 404,
    refusal: error?.status === 403 ? (refusalOf(error.body) ?? { canRequest: false, requestPending: false }) : null,
    failed: list.isError && error?.status !== 403 && error?.status !== 404,
    form: { ...form, date: details.date },
    onForm: (patch: Partial<typeof form>) => setForm(f => ({ ...f, ...patch })),
    uploading: send.isPending,
    failure,
    lastUpload,
    onFile: (file: File) => {
      const problem = fileProblem(file);
      if (problem) { setRetry(null); setFailure(problem); return; }
      submit({ file, pages: false });
    },
    canRetry: retry !== null,
    onRetry: () => retry && submit(retry),
    onDismissFailure: () => { setFailure(null); setRetry(null); },
    pages: pages.length,
    addingPhoto: addPhoto.isPending,
    photoFailed,
    onPhoto: (photo: File) => addPhoto.mutate(photo),
    onClearPages: () => setPages([]),
    onSavePages: () => submit({ file: new Blob([buildPdf(pages) as BlobPart], { type: 'application/pdf' }), pages: true }),
    viewer,
    expired,
    opening: open.isPending ? (open.variables ?? null) : null,
    openProblem: openError ? (openError.code === 'scan_pending' ? ('scan_pending' as const) : openError.code === 'quarantined' ? ('quarantined' as const) : ('failed' as const)) : null,
    onOpen: (id: string) => open.mutate(id),
    editing,
    saving: edit.isPending,
    editFailed: edit.isError,
    onEdit: (d: { id: string; type: DocumentType; documentDate: string }) => { edit.reset(); setEditing({ id: d.id, type: d.type, date: d.documentDate }); },
    onEditChange: (patch: { type?: DocumentType; date?: string }) => setEditing(e => e && { ...e, ...patch }),
    onSaveEdit: () => editing && edit.mutate(editing),
    onCancelEdit: () => setEditing(null),
    onCloseViewer: () => { setViewer(null); setExpired(false); },
  };
}
