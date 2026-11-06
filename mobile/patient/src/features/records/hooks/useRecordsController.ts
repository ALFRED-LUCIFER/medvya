import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { errorKey } from '../../api/domain/errors';
import { useSignedIn } from '../../session/hooks/session';
import type { RecordsTab } from '../domain/records';
import { fetchRecords } from '../services/records';

export function useRecordsController() {
  const { session } = useSignedIn();
  const [tab, setTab] = useState<RecordsTab>('visits');
  const query = useQuery({ queryKey: ['records', session.person.id], queryFn: () => fetchRecords(session.person.id) });
  return {
    session,
    tab,
    onTab: setTab,
    records: query.data ?? null,
    loading: query.isPending,
    error: query.error ? errorKey(query.error) : null,
  };
}
