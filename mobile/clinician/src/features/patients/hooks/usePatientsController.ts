import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { errorKey } from '../../api/domain/errors';
import { useSignedIn } from '../../session/hooks/session';
import { matchPatient } from '../domain/patients';
import { fetchPatients } from '../services/patients';

export function usePatientsController() {
  const { session } = useSignedIn();
  const [query, setQuery] = useState('');
  const list = useQuery({ queryKey: ['patients', session.facility.id], queryFn: fetchPatients });
  return {
    session,
    query,
    onQuery: setQuery,
    patients: (list.data ?? []).filter(p => matchPatient(p, query)),
    loading: list.isPending,
    error: list.error ? errorKey(list.error) : null,
  };
}
