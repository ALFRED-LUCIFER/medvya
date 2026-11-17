import { post, send } from '../../api/services/apiClient';
import { downloadPaperPdf } from '../../prescription/services/prescriptionApi';
import type { CashCount, CashDay, CashSession } from '../domain/dayClose';

// The day close (MDV-P4-012). Ids in paths; amounts, counts and notes in bodies.
const base = '/v1/billing/cash-sessions';

export const fetchCashDay = () => send<CashDay>(base);
export const openCashSession = (openingFloatMinor: number) => post<CashSession>(base, { openingFloatMinor });
export const countCashSession = (id: string, body: { counts: CashCount[]; note: string | null; version: number }) => post<CashSession>(`${base}/${id}/count`, body);
export const confirmCashSession = (id: string, version: number) => post<CashSession>(`${base}/${id}/confirm`, { version });
/** The shift report, saved under a name with no patient or staff data. */
export const downloadShiftReport = (id: string) => downloadPaperPdf(`${base}/${id}/report/pdf`, 'shift-report.pdf');
