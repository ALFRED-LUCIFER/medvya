export type AuditEntry = {
  id: string; occurredAt: string; action: string; resourceType: string; resourceId: string | null; subjectTenantId: string | null;
  actor: string | null; reason: string | null;
};

/** Actions the console writes today, each with words in the catalogue, so no raw code reaches the screen. */
const actionKeys = {
  'auth.signon': 'platform.audit.action.auth_signon',
  'auth.signin': 'platform.audit.action.auth_signin',
  'auth.signout': 'platform.audit.action.auth_signout',
  'auth.key.refused': 'platform.audit.action.auth_key_refused',
  'key.register': 'platform.audit.action.key_register',
  'key.recovery.issue': 'platform.audit.action.key_recovery_issue',
  'team.bootstrap': 'platform.audit.action.team_bootstrap',
  'team.invite': 'platform.audit.action.team_invite',
  'team.remove': 'platform.audit.action.team_remove',
  'team.access_review': 'platform.audit.action.team_access_review',
  'audit.export': 'platform.audit.action.audit_export',
  'organisation.approve': 'platform.audit.action.organisation_approve',
  'organisation.request-changes': 'platform.audit.action.organisation_request_changes',
  'organisation.reject': 'platform.audit.action.organisation_reject',
  'organisation.suspend': 'platform.audit.action.organisation_suspend',
  'organisation.reinstate': 'platform.audit.action.organisation_reinstate',
  'organisation.document.view': 'platform.audit.action.organisation_document_view',
} as const;
export type KnownAction = keyof typeof actionKeys;
export const knownActions = Object.keys(actionKeys) as KnownAction[];

export const isKnownAction = (a: string): a is KnownAction => a in actionKeys;

export const actionKey = (a: KnownAction) => actionKeys[a];

export const pageSize = 50;

/** Column definitions are data: each is its heading's i18n key. The time identifies a row (MDV-P2-054). */
export const auditColumns = ['platform.audit.when', 'platform.audit.what', 'platform.audit.who', 'platform.audit.reason'] as const;
