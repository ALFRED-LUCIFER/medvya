// GET /v1/me/sessions (SessionControl.cs): the caller's own live sessions. Device is a browser and system label; no patient data.
export type DeviceSession = { id: string; device: string; createdAt: string; lastSeenAt: string; current: boolean };

/** This device's own session is ended by Sign out (which also ends the provider session, #912), so it never gets a Sign out here. */
export const canEnd = (s: Pick<DeviceSession, 'current'>) => !s.current;

/** This device first, then the most recently active. */
export const orderDevices = (list: DeviceSession[]) =>
  [...list].sort((a, b) => Number(b.current) - Number(a.current) || b.lastSeenAt.localeCompare(a.lastSeenAt));
