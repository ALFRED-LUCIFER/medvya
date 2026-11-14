import { patientDirectory } from '../../shell/domain/navigation';

export type RecordTab = 'summary' | 'details' | 'timeline' | 'documents';

/** What this role may already open of a patient; the routes and the server decide the same, so a tab never widens access. */
export type RecordAccess = {
  /** Summary and Timeline: the clinical record (Patient 360 at a clinician's level). */
  clinical: boolean;
  /** Details: the registration record in the directory (Register / book patients at Read or Full). */
  details: boolean;
  /** Documents: clinicians, and the front desk (upload and list only). */
  documents: boolean;
};

/**
 * The one Patient record (MDV-P2-051): Summary for clinicians, Details for the desk, then Timeline and Documents, each only when the role
 * may already open it. The addresses are the existing screens', so old links keep working.
 */
export function recordTabs(patientId: string, access: RecordAccess): { tab: RecordTab; path: string }[] {
  const tabs: [RecordTab, string, boolean][] = [
    ['summary', `/patients/${patientId}`, access.clinical],
    ['details', `${patientDirectory.path}/${patientId}`, access.details],
    ['timeline', `/patients/${patientId}/timeline`, access.clinical],
    ['documents', `/patients/${patientId}/docs`, access.documents],
  ];
  return tabs.filter(([, , may]) => may).map(([tab, path]) => ({ tab, path }));
}
