import { describe, expect, it } from 'vitest';
import { anyPending, canOpenDocument, fileProblem, maxBytes, todayIn, uploadFailure, type DocumentRow } from './documents';
import { buildPdf } from './pdf';

const doc = (scanStatus: DocumentRow['scanStatus']): DocumentRow =>
  ({ id: 'd1', type: 'LabReport', documentDate: '2026-10-01', source: 'Imported', scanStatus, contentType: 'application/pdf', sizeBytes: 10, uploadedBy: null, uploadedAt: '2026-10-01T04:30:00Z' });

describe('fileProblem', () => {
  it('accepts pdf, jpeg and png up to the limit', () => {
    for (const type of ['application/pdf', 'image/jpeg', 'image/png']) expect(fileProblem({ size: 1000, type })).toBeNull();
    expect(fileProblem({ size: maxBytes, type: 'application/pdf' })).toBeNull();
  });
  it('names what is wrong', () => {
    expect(fileProblem({ size: 0, type: 'application/pdf' })).toBe('empty');
    expect(fileProblem({ size: maxBytes + 1, type: 'application/pdf' })).toBe('too_large');
    expect(fileProblem({ size: 5, type: 'text/html' })).toBe('unsupported_type');
  });
});

describe('uploadFailure', () => {
  it('reads the server answer', () => {
    expect(uploadFailure(413, undefined)).toBe('too_large');
    expect(uploadFailure(415, 'unsupported_type')).toBe('unsupported_type');
    expect(uploadFailure(400, 'empty')).toBe('empty');
    expect(uploadFailure(400, undefined)).toBe('invalid');
    expect(uploadFailure(403, 'no_access')).toBe('forbidden');
    expect(uploadFailure(503, undefined)).toBe('unavailable');
    expect(uploadFailure(500, undefined)).toBe('failed');
    expect(uploadFailure(undefined, undefined)).toBe('failed');
  });
});

describe('opening', () => {
  it('needs the role and a passed scan', () => {
    expect(canOpenDocument(doc('Clean'), true)).toBe(true);
    expect(canOpenDocument(doc('Clean'), false)).toBe(false); // the front desk
    expect(canOpenDocument(doc('Pending'), true)).toBe(false);
    expect(canOpenDocument(doc('Infected'), true)).toBe(false);
  });
  it('polls only while a scan is running', () => {
    expect(anyPending([doc('Clean'), doc('Pending')])).toBe(true);
    expect(anyPending([doc('Clean'), doc('Infected')])).toBe(false);
  });
});

it('todayIn is the date in the facility zone, not the device zone', () => {
  const instant = new Date('2026-10-19T20:00:00Z'); // already the 20th in India
  expect(todayIn('Asia/Kolkata', instant)).toBe('2026-10-20');
  expect(todayIn('America/New_York', instant)).toBe('2026-10-19');
});

describe('buildPdf', () => {
  const jpeg = (n: number) => new Uint8Array([0xff, 0xd8, 0xff, 0xe0, ...Array.from({ length: n }, (_, i) => i % 256), 0xff, 0xd9]);
  const text = (b: Uint8Array) => new TextDecoder('latin1').decode(b);

  it('makes a PDF the server will accept, with one page per image', () => {
    const bytes = buildPdf([{ jpeg: jpeg(300), width: 1200, height: 1600 }, { jpeg: jpeg(50), width: 800, height: 600 }]);
    const s = text(bytes);
    expect(s.startsWith('%PDF-')).toBe(true);
    expect(s.trimEnd().endsWith('%%EOF')).toBe(true);
    expect(s).toContain('/Count 2');
    expect(s.match(/\/Type \/Page /g)).toHaveLength(2);
    expect(s).toContain('/MediaBox [0 0 595 793]'); // 1200x1600 scaled to A4 width
  });

  it('has a cross-reference table whose offsets point at the objects', () => {
    const bytes = buildPdf([{ jpeg: jpeg(100), width: 100, height: 100 }]);
    const s = text(bytes);
    const start = Number(/startxref\n(\d+)/.exec(s)![1]);
    expect(s.slice(start, start + 4)).toBe('xref');
    const entries = [...s.slice(start).matchAll(/^(\d{10}) 00000 n $/gm)].map(m => Number(m[1]));
    expect(entries).toHaveLength(5); // catalog, tree, page, content, image
    entries.forEach((offset, i) => expect(s.slice(offset, offset + 8)).toBe(`${i + 1} 0 obj\n`));
  });

  it('keeps the JPEG bytes exactly as captured', () => {
    const page = jpeg(64);
    const bytes = buildPdf([{ jpeg: page, width: 10, height: 10 }]);
    const at = bytes.findIndex((_, i) => page.every((b, j) => bytes[i + j] === b));
    expect(at).toBeGreaterThan(0);
  });

  it('needs at least one page', () => expect(() => buildPdf([])).toThrow());
});
