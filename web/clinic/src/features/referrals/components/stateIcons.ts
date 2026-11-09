import { CalendarCheck, FileText, Inbox, Lock, Pencil, Send } from 'lucide-react';
import type { ReferralState } from '../domain/referrals';

/** A status is an icon and a word, never colour alone. */
export const stateIcons: Record<ReferralState, typeof Pencil> = { Draft: Pencil, Sent: Send, AppointmentBooked: CalendarCheck, ReportReceived: FileText, Closed: Lock, Received: Inbox };
