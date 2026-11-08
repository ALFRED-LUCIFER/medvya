import type { PageImage } from '../domain/pdf';

const MAX_SIDE = 1600;

/**
 * One camera photo as a page: scaled so its long side is at most 1600 px and re-encoded as JPEG (phone photos are several megabytes, and a few
 * pages together must stay under the upload limit). Uses the browser's image and canvas APIs; nothing leaves the device here.
 */
export async function toPageImage(photo: Blob): Promise<PageImage> {
  const bitmap = await createImageBitmap(photo, { imageOrientation: 'from-image' });
  try {
    const scale = Math.min(1, MAX_SIDE / Math.max(bitmap.width, bitmap.height));
    const [width, height] = [Math.round(bitmap.width * scale), Math.round(bitmap.height * scale)];
    const canvas = document.createElement('canvas');
    canvas.width = width;
    canvas.height = height;
    canvas.getContext('2d')!.drawImage(bitmap, 0, 0, width, height);
    const jpeg = await new Promise<Blob | null>(resolve => canvas.toBlob(resolve, 'image/jpeg', 0.8));
    if (!jpeg) throw new Error('The photo could not be read.');
    return { jpeg: new Uint8Array(await jpeg.arrayBuffer()), width, height };
  } finally {
    bitmap.close();
  }
}
