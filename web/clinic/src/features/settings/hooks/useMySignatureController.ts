import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ApiError } from '../../schedule/services/scheduleApi';
import { doctorProblem, signatureProblem } from '../domain/doctors';
import { fetchMySignature, removeMySignature, uploadMySignature } from '../services/doctorsApi';

/** The signed-in doctor's own signature image (#1365). `hasSignature` is null while loading and for someone with no doctor's profile here. */
export function useMySignatureController(enabled: boolean) {
  const queryClient = useQueryClient();
  const query = useQuery({ queryKey: ['me', 'signature'], queryFn: fetchMySignature, enabled });
  const [problem, setProblem] = useState<'type' | 'size' | 'failed' | null>(null);
  const done = () => { setProblem(null); void queryClient.invalidateQueries({ queryKey: ['me', 'signature'] }); };
  const upload = useMutation({
    mutationFn: uploadMySignature,
    onSuccess: done,
    onError: e => {
      const code = e instanceof ApiError ? doctorProblem(e.code) : 'failed';
      setProblem(code === 'signature:type' ? 'type' : code === 'signature:too_large' ? 'size' : 'failed');
    },
  });
  const remove = useMutation({ mutationFn: removeMySignature, onSuccess: done, onError: () => setProblem('failed') });
  return {
    hasSignature: query.data?.hasSignature ?? null,
    problem,
    busy: upload.isPending || remove.isPending,
    onChoose: (file: File | undefined) => {
      if (!file) return;
      const bad = signatureProblem(file);
      if (bad) setProblem(bad);
      else upload.mutate(file);
    },
    onRemove: () => remove.mutate(),
  };
}
