import { describe, expect, it } from 'vitest';
import { authoriseProblem, isCurrent, pdfProblem, qrCells, type RxAuthorisation } from './authorisation';

describe('prescription authorisation (MDV-P2-017)', () => {
  it('reads the server refusal as a problem the panel can word', () => {
    expect(authoriseProblem(403, 'reauth_required')).toBe('reauth');
    expect(authoriseProblem(403, 'no_care_relationship')).toBe('failed');
    expect(authoriseProblem(409, 'unresolved_alerts')).toBe('unresolved_alerts');
    expect(authoriseProblem(400, 'lines:no_instructions')).toBe('no_instructions');
    expect(authoriseProblem(500, undefined)).toBe('failed');
  });

  it('treats only an authorised, unchanged, not cancelled prescription as current', () => {
    const v1: RxAuthorisation = { versionNo: 1, number: 'RX-1', authorisedBy: 'u', authorisedAt: '2026-11-03T04:00:00Z', contentHash: 'h', lines: 1, cancellation: null };
    expect(isCurrent(null)).toBe(false);
    expect(isCurrent({ authorised: null, changed: false })).toBe(false);
    expect(isCurrent({ authorised: v1, changed: false })).toBe(true);
    expect(isCurrent({ authorised: v1, changed: true })).toBe(false);
    expect(isCurrent({ authorised: { ...v1, cancellation: { reason: 'r', cancelledBy: 'u', cancelledAt: v1.authorisedAt } }, changed: false })).toBe(false);
  });

  it('draws only the dark modules of the QR code', () => {
    expect(qrCells(['010', '100'])).toEqual([[1, 0], [0, 1]]);
  });

  it('tells a script the PDF cannot show from a failure', () => {
    expect(pdfProblem(422)).toBe('unsupported');
    expect(pdfProblem(500)).toBe('failed');
  });
});
