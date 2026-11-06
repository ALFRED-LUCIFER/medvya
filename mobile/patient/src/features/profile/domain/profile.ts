export type Choice = { key: 'reminders' | 'sharing' | 'recording' | 'research'; on: boolean; detail: string };
export type Profile = { insurance: string | null; choices: Choice[] };
