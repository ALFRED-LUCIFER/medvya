import { z } from 'zod';
import type { OrganisationState } from '../../session/domain/session';

// Shapes mirror src/Modules/Tenancy/Medvya.Tenancy.Application/IOrganisationRequests.cs. The server is the authority; the checks here answer at once.
export type OrganisationType = 'Clinic' | 'Hospital';
export type DocumentKind = 'RegistrationCertificate' | 'HospitalRegistration' | 'TaxRegistration' | 'OwnerId' | 'MedicalCouncilRegistration' | 'SignatoryLetter' | 'Dpa';
export type OrganisationDocument = { id: string; kind: DocumentKind; contentType: string; sizeBytes: number; uploadedAt: string };
export type OrganisationRequest = {
  type: OrganisationType; state: OrganisationState; reason: string | null; itContactName: string | null; itContactEmail: string | null; submittedAt: string | null;
  requiredDocuments: DocumentKind[]; documents: OrganisationDocument[];
};

/** The request can be edited and sent only in these states (the server enforces it: "request:locked"). */
export const isEditable = (state: OrganisationState) => state === 'Draft' || state === 'NeedsChanges';

export const maxDocumentBytes = 5 * 1024 * 1024;
export type FileProblem = 'type' | 'size';
/** Quick check before upload; the server decides from the file's own bytes. */
export const fileProblem = (file: { type: string; size: number }): FileProblem | null =>
  !['application/pdf', 'image/png', 'image/jpeg'].includes(file.type) ? 'type' : file.size === 0 || file.size > maxDocumentBytes ? 'size' : null;

/** Required kinds with no file yet, in the order the server lists them. */
export const missingKinds = (r: OrganisationRequest): DocumentKind[] =>
  r.requiredDocuments.filter(kind => !r.documents.some(d => d.kind === kind));

/** Hospital only: the person Medvya talks to about security. */
export const itContactSchema = z.object({
  name: z.string().trim().min(1, 'required').max(200, 'tooLong'),
  email: z.string().trim().min(1, 'required').regex(/^[^\s@]+@[^\s@]+\.[^\s@]+$/, 'format'),
});
export type ItContactForm = z.infer<typeof itContactSchema>;

/** Whether the request can be submitted now: every document is there, and a Hospital has its IT contact. */
export const canSubmit = (r: OrganisationRequest) =>
  isEditable(r.state) && missingKinds(r).length === 0 && (r.type !== 'Hospital' || itContactSchema.safeParse({ name: r.itContactName ?? '', email: r.itContactEmail ?? '' }).success);

export const documentErrors = ['kind:unknown', 'file:type', 'file:too_large', 'request:locked'] as const;
export type RequestError = (typeof documentErrors)[number] | 'failed';
export const requestError = (code: string | undefined): RequestError => (documentErrors as readonly string[]).includes(code ?? '') ? (code as RequestError) : 'failed';

/** The message key suffix for a refused upload: the quick check here, or the server's code. */
export const uploadErrorKey = (e: RequestError | FileProblem): 'type' | 'size' | 'locked' | 'failed' =>
  e === 'type' || e === 'file:type' ? 'type' : e === 'size' || e === 'file:too_large' ? 'size' : e === 'request:locked' ? 'locked' : 'failed';

/** Where the owner sees the request; the shell's notice links here. */
export const organisationPath = '/organisation';
