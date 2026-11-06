// shortcut: duplicated in mobile/patient; both move to packages/auth-client with native tokens (MDV-P5-033).
/** An error as an i18n key: services throw keys (`mobile.…`); anything else is a generic failure. */
export const errorKey = (e: unknown) => (e instanceof Error && e.message.startsWith('mobile.') ? e.message : 'mobile.error.generic');
