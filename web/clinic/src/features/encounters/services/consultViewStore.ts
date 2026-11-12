import type { ConsultView } from '../domain/encounter';

// The doctor's chosen consultation view (MDV-P2-041), a per-device convenience: no patient data, and the page works without storage.
const key = 'medvya.consultView';

export function loadConsultView(): ConsultView {
  try { return localStorage.getItem(key) === 'steps' ? 'steps' : 'page'; } catch { return 'page'; }
}

export function saveConsultView(view: ConsultView) {
  try { localStorage.setItem(key, view); } catch { /* storage blocked: the choice lasts until the page closes */ }
}
