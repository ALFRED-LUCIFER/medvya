import { apiFetch } from '../../api/services/apiClient';
import { startSignInWithHint } from '../../session/services/sessionApi';
import type { Country } from '@medvya/i18n';
import type { Draft, FinishResult, OnboardingData } from '../domain/onboarding';

const json = { 'Content-Type': 'application/json', 'X-Medvya-Csrf': '1' };

/** The server refused the draft: errors maps a field path (e.g. "licence.number") to stable codes. */
export class ValidationError extends Error {
  readonly errors: Record<string, string[]>;
  constructor(errors: Record<string, string[]>) { super('validation'); this.errors = errors; }
}

export class ApiError extends Error {
  readonly status: number;
  constructor(status: number, what: string) { super(`${what} ${status}`); this.status = status; }
}

async function check(res: Response, what: string) {
  if (res.status === 400) {
    const body = (await res.json().catch(() => null)) as { errors?: Record<string, string[]> } | null;
    if (body?.errors) throw new ValidationError(body.errors);
  }
  if (!res.ok) throw new ApiError(res.status, what);
}

/** The saved draft, or null when the person has not started (or has finished). */
export async function fetchDraft(): Promise<Draft | null> {
  const res = await apiFetch('/v1/onboarding', { credentials: 'same-origin' });
  if (res.status === 204) return null;
  await check(res, 'onboarding');
  return res.json() as Promise<Draft>;
}

export async function startDraft(country: Country): Promise<Draft> {
  const res = await apiFetch('/v1/onboarding', { method: 'POST', credentials: 'same-origin', headers: json, body: JSON.stringify({ country }) });
  await check(res, 'onboarding start');
  const draft = (await res.json()) as Draft;
  // The create-account answers (MDV-P1-059) become the new draft's first values, once.
  const prefill = takePrefill();
  if (!prefill) return draft;
  try {
    await saveDraft(1, prefill);
    return { ...draft, data: prefill };
  } catch { return draft; } // only a convenience: the draft exists, so the wizard opens empty rather than failing to start
}

// Per tab, the owner's own answers only (practice name, city, their name and role): no patient data, removed as soon as it is read.
const prefillKey = 'medvya.signUp';

function takePrefill(): OnboardingData | null {
  try {
    const raw = sessionStorage.getItem(prefillKey);
    sessionStorage.removeItem(prefillKey);
    return raw ? (JSON.parse(raw) as OnboardingData) : null;
  } catch { return null; } // storage blocked or unreadable: the wizard simply starts empty
}

/** Saves the draft and the step to resume at. With `validate` the server checks that step first and saves nothing if it fails. */
export async function saveDraft(step: number, data: OnboardingData, validate?: number) {
  const res = await apiFetch('/v1/onboarding', { method: 'PUT', credentials: 'same-origin', headers: json, body: JSON.stringify({ step, validate, data }) });
  await check(res, 'onboarding save');
}

export async function finishOnboarding(): Promise<FinishResult> {
  const res = await apiFetch('/v1/onboarding/finish', { method: 'POST', credentials: 'same-origin', headers: json });
  await check(res, 'onboarding finish');
  return res.json() as Promise<FinishResult>;
}

/**
 * Sign up: the provider's MFA sign-in, then back to the wizard. The server creates the account only on this path. The email goes to the
 * provider as a login hint, in a POST body (startSignInWithHint); the other answers wait in this tab for the draft (startDraft).
 */
export function startSignUp(country: Country, email?: string, prefill?: OnboardingData) {
  if (prefill) try { sessionStorage.setItem(prefillKey, JSON.stringify(prefill)); } catch { /* storage blocked: the wizard asks again */ }
  const returnUrl = `${import.meta.env.BASE_URL}onboarding?country=${country}`;
  if (email) void startSignInWithHint({ loginHint: email, returnUrl, signup: true });
  else window.location.assign(`/bff/login?signup=true&returnUrl=${encodeURIComponent(returnUrl)}`);
}
