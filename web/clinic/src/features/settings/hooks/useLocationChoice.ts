import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { fetchLocations } from '../services/settingsApi';

/** The tenant's locations and the one a tab is looking at (the location the user works at until they pick another). */
export function useLocationChoice(defaultId: string) {
  const [picked, setPicked] = useState<string | null>(null);
  const list = useQuery({ queryKey: ['settings', 'locations'], queryFn: fetchLocations });
  const locations = list.data ?? [];
  const locationId = picked ?? (locations.some(l => l.id === defaultId) ? defaultId : locations[0]?.id ?? defaultId);
  return { locations, locationId, onLocation: setPicked, locationsLoading: list.isPending, locationsFailed: list.isError };
}
