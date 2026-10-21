/** What /v1/me/context returns today; memberships and permissions arrive with the facility picker (MDV-P0-012). */
export type MeContext = { user: { id: string; displayName: string } };
