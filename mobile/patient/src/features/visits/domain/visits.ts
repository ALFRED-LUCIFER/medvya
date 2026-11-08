export type Visit = { id: string; at: string; doctor: string; reason: string; status: 'booked' | 'completed' | 'cancelled' };

/** Upcoming (soonest first) and past (latest first). A booked visit whose time has passed counts as past. */
export function splitVisits(visits: Visit[], now: Date) {
  const t = now.toISOString();
  const upcoming = visits.filter(v => v.status === 'booked' && v.at >= t).sort((a, b) => a.at.localeCompare(b.at));
  const past = visits.filter(v => !upcoming.includes(v)).sort((a, b) => b.at.localeCompare(a.at));
  return { upcoming, past };
}
