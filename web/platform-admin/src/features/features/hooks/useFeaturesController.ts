import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { ApiError } from '../../api/services/api';
import type { PlatformRole } from '../../session/domain/session';
import { reasonForm, type ReasonForm } from '../../team/domain/team';
import { bodyOf, canChange, formFor, releaseForm, type PendingChange, type Release, type ReleaseForm } from '../domain/features';
import { fetchReleases, setRelease, turnOffEverywhere } from '../services/featuresApi';

const releasesKey = ['platform', 'features'] as const;

// The server's refusals that belong to one field of the change form, as the form's own error codes.
const fieldCodes = { 'clinicalSignOff:required': 'signOff', 'percentage:range': 'percentage', 'organisations:required': 'required', 'organisations:too_many': 'tooMany' } as const;

/** The Features page (MDV-P5-041): Medvya's release of each feature per country, a new stage with its reason, and turning a feature off everywhere. */
export function useFeaturesController(role: PlatformRole) {
  const client = useQueryClient();
  const releases = useQuery({ queryKey: releasesKey, queryFn: fetchReleases });
  const [chosen, setChosen] = useState<string | null>(null);
  const [pending, setPending] = useState<PendingChange | null>(null);
  const change = useForm<ReleaseForm>({ resolver: zodResolver(releaseForm), shouldFocusError: false });
  const off = useForm<ReasonForm>({ resolver: zodResolver(reasonForm), defaultValues: { reason: '' }, shouldFocusError: false });
  const stage = useWatch({ control: change.control, name: 'stage' });

  const done = () => { setPending(null); void client.invalidateQueries({ queryKey: releasesKey }); };
  const save = useMutation({
    mutationFn: ({ country, key, form }: { country: string; key: string; form: ReleaseForm }) => setRelease(country, key, bodyOf(form)),
    onSuccess: done,
    onError: e => {
      if (!(e instanceof ApiError)) return;
      for (const [field, codes] of Object.entries(e.fields))
        for (const code of codes) {
          const mine = fieldCodes[`${field}:${code}` as keyof typeof fieldCodes];
          if (mine) change.setError(field as keyof ReleaseForm, { message: mine });
        }
    },
  });
  const turnOff = useMutation({ mutationFn: ({ key, reason }: { key: string; reason: string }) => turnOffEverywhere(key, reason), onSuccess: done });

  const countries = releases.data?.map(c => c.country) ?? [];
  const country = chosen && countries.includes(chosen) ? chosen : countries[0] ?? null;
  const failed = save.isError ? save.error : turnOff.isError ? turnOff.error : null;
  const fieldRefusal = failed instanceof ApiError && Object.keys(change.formState.errors).length > 0;

  return {
    state: releases.isPending ? 'loading' as const : releases.isError ? 'error' as const : 'ready' as const,
    forbidden: releases.error instanceof ApiError && releases.error.status === 403,
    countries, country, onCountry: setChosen,
    rows: releases.data?.find(c => c.country === country)?.features ?? [],
    canChange: canChange(role),
    pending, changeForm: change, offForm: off, busy: save.isPending || turnOff.isPending,
    /** The stage being chosen, so the form asks only for what it needs (a share, organisations, a sign-off). */
    stage,
    /** A refusal no field shows (locked, gone, failed); field refusals sit on their field. */
    problem: !failed || fieldRefusal ? null : failed instanceof ApiError && failed.fields.stage?.includes('locked') ? 'locked' as const : 'failed' as const,
    onStartChange: (release: Release) => {
      if (!country) return;
      save.reset(); turnOff.reset(); change.reset(formFor(release));
      setPending({ kind: 'change', country, release });
    },
    onStartOff: (release: Release) => { save.reset(); turnOff.reset(); off.reset({ reason: '' }); setPending({ kind: 'off', release }); },
    onCancel: () => setPending(null),
    onSave: change.handleSubmit(form => { if (pending?.kind === 'change') save.mutate({ country: pending.country, key: pending.release.key, form }); }),
    onTurnOff: off.handleSubmit(({ reason }) => { if (pending?.kind === 'off') turnOff.mutate({ key: pending.release.key, reason }); }),
  };
}
