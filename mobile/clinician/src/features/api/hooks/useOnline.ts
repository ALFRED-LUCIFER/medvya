import { onlineManager } from '@tanstack/react-query';
import { useSyncExternalStore } from 'react';

/** Whether the phone is online, as TanStack Query sees it (fed by services/network.ts). */
export const useOnline = () => useSyncExternalStore(l => onlineManager.subscribe(l), () => onlineManager.isOnline());
