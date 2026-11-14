import { useSyncExternalStore } from 'react';
import { isPhone, onPhoneChange } from '../services/viewport';

/** True on a phone (under 768 px with a touch pointer), following the window as it resizes or rotates (the phone rule, MDV-P1-098). */
export const usePhone = () => useSyncExternalStore(onPhoneChange, isPhone);
