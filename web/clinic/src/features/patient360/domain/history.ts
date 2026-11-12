import { z } from 'zod';
import type { HistoryEntry, HistorySection } from './chart';

// History sections (MDV-P2-032). The server keeps every version and decides who may save; this file only groups and shapes the form.
export const maxHistoryText = 2000;
export const bloodGroups = ['A+', 'A-', 'B+', 'B-', 'AB+', 'AB-', 'O+', 'O-'] as const;

export type SectionView = { section: HistorySection; current: HistoryEntry | null; earlier: HistoryEntry[] };

/** The offered sections in their order, then any other section already written (never hidden), each with its current text and earlier versions. */
export function historySections(offered: HistorySection[], entries: HistoryEntry[]): SectionView[] {
  const written = [...new Set(entries.map(e => e.section))].filter(s => s !== 'BloodGroup' && !offered.includes(s));
  return [...offered, ...written].map(section => {
    const [current = null, ...earlier] = entries.filter(e => e.section === section);
    return { section, current, earlier };
  });
}

export const historySchema = z.object({ text: z.string().trim().min(1).max(maxHistoryText) });
export type HistoryForm = z.infer<typeof historySchema>;
export const bloodGroupSchema = z.object({ text: z.enum(bloodGroups) });
export type BloodGroupForm = z.infer<typeof bloodGroupSchema>;
