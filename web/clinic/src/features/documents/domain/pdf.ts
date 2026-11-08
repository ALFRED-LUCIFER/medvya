/** One captured page: a JPEG and its size in pixels. */
export type PageImage = { jpeg: Uint8Array; width: number; height: number };

const A4_WIDTH = 595;

/**
 * Several camera pages as one PDF, one page per image, each scaled to A4 width (MDV-P2-006, "scan to patient file"). The JPEG bytes go in as
 * they are (DCTDecode), so nothing is re-encoded and no PDF library is needed. Throws with no pages.
 */
export function buildPdf(pages: PageImage[]): Uint8Array {
  if (pages.length === 0) throw new Error('A PDF needs at least one page.');
  const enc = new TextEncoder();
  const chunks: Uint8Array[] = [];
  const offsets: number[] = [];
  let length = 0;
  const add = (part: string | Uint8Array) => {
    const bytes = typeof part === 'string' ? enc.encode(part) : part;
    chunks.push(bytes);
    length += bytes.length;
  };
  const object = (n: number, body: (string | Uint8Array)[]) => {
    offsets[n] = length;
    add(`${n} 0 obj\n`);
    body.forEach(add);
    add('\nendobj\n');
  };

  // Objects: 1 catalog, 2 page tree, then per page 3 objects (page, content, image) starting at 3.
  add('%PDF-1.4\n%\xE2\xE3\xCF\xD3\n');
  object(1, ['<< /Type /Catalog /Pages 2 0 R >>']);
  object(2, [`<< /Type /Pages /Count ${pages.length} /Kids [${pages.map((_, i) => `${3 + i * 3} 0 R`).join(' ')}] >>`]);
  pages.forEach((p, i) => {
    const [page, content, image] = [3 + i * 3, 4 + i * 3, 5 + i * 3];
    const h = Math.round((p.height * A4_WIDTH) / p.width);
    const draw = `q ${A4_WIDTH} 0 0 ${h} 0 0 cm /Im0 Do Q`;
    object(page, [`<< /Type /Page /Parent 2 0 R /MediaBox [0 0 ${A4_WIDTH} ${h}] /Resources << /XObject << /Im0 ${image} 0 R >> >> /Contents ${content} 0 R >>`]);
    object(content, [`<< /Length ${draw.length} >>\nstream\n${draw}\nendstream`]);
    object(image, [`<< /Type /XObject /Subtype /Image /Width ${p.width} /Height ${p.height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length ${p.jpeg.length} >>\nstream\n`, p.jpeg, '\nendstream']);
  });

  const count = 3 + pages.length * 3;
  const xref = length;
  add(`xref\n0 ${count}\n0000000000 65535 f \n`);
  for (let n = 1; n < count; n++) add(`${String(offsets[n]).padStart(10, '0')} 00000 n \n`);
  add(`trailer\n<< /Size ${count} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`);

  const out = new Uint8Array(length);
  let at = 0;
  for (const c of chunks) { out.set(c, at); at += c.length; }
  return out;
}
