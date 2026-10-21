import {
  Calendar, CalendarDays, ClipboardCheck, Forward, Headset, Inbox, MessageSquare, Mic, ReceiptText,
  SlidersHorizontal, Syringe, UserPlus, Users, Video, type LucideIcon,
} from 'lucide-react';
import type { NavGroup, NavId } from '../domain/navigation';

// Icons follow the Clinician App sidebar (Material symbols mapped to Lucide).
export const navIcons: Record<NavId, LucideIcon> = {
  today: CalendarDays, patients: Users, messages: MessageSquare, video: Video, referrals: Forward, inbox: Inbox,
  carePlans: ClipboardCheck, frontDesk: Headset, schedule: Calendar, register: UserPlus, nurse: Syringe,
  billing: ReceiptText, devices: Mic, settings: SlidersHorizontal,
};

/** Module rail colour beside the current item (tokens in packages/ui/src/styles.css). */
export const railColour: Record<NavGroup, string> = {
  clinical: 'before:bg-rail-clinical', practice: 'before:bg-rail-practice', assistant: 'before:bg-rail-assistant', admin: 'before:bg-rail-admin',
};
