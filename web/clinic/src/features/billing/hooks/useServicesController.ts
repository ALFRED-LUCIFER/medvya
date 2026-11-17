import { useState } from 'react';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { useI18n } from '@medvya/i18n';
import { ApiError } from '../../schedule/services/scheduleApi';
import { fetchDoctors } from '../../settings/services/doctorsApi';
import { useTableController } from '../../table/hooks/useTableController';
import {
  catalogueProblem, emptyService, priceFor, priceSchema, serviceSchema, serviceSort, toPriceBody, todayIn, toServiceBody, toServiceForm, upcomingFor,
  type CatalogueProblem, type PriceForm, type PriceRow, type Service, type ServiceForm,
} from '../domain/catalogue';
import { addPrice, archiveService, createService, fetchPrices, fetchServices, updateService } from '../services/billingApi';


const problemOf = (e: unknown) => catalogueProblem(e instanceof ApiError ? e.code : undefined);

/**
 * Settings, Services (MDV-P4-002): the catalogue, and an editor for one service with its prices per doctor. Saving a price adds a version from a day in
 * the facility's time zone; the server checks every value and the caller's capability.
 */
export function useServicesController(timeZone: string) {
  const { t } = useI18n();
  const queryClient = useQueryClient();
  const [archived, setArchived] = useState(false);
  const list = useQuery({ queryKey: ['billing', 'services', archived], queryFn: () => fetchServices(archived) });
  const services = list.data ?? [];
  const table = useTableController(services, serviceSort, c => t(`billing.services.col.${c}`));

  // The editor: a new service, or one from the list (kept fresh from the list after each save).
  const [editing, setEditing] = useState<'new' | string | null>(null);
  const [now, setNow] = useState(() => new Date()); // prices in effect now; moved on after each save so a price from today shows as current
  const service = editing && editing !== 'new' ? services.find(s => s.id === editing) ?? null : null;
  const form = useForm<ServiceForm>({ resolver: zodResolver(serviceSchema), defaultValues: emptyService });
  const [formError, setFormError] = useState<CatalogueProblem | null>(null);
  const today = todayIn(timeZone);
  const priceForm = useForm<PriceForm>({ resolver: zodResolver(priceSchema), defaultValues: { amount: '', effectiveOn: today } });
  const [priceDoctor, setPriceDoctor] = useState<string | null>(null);
  const [priceError, setPriceError] = useState<CatalogueProblem | null>(null);

  const prices = useQuery({ queryKey: ['billing', 'prices', service?.id], queryFn: () => fetchPrices(service!.id), enabled: !!service });
  const doctors = useQuery({ queryKey: ['settings', 'doctors'], queryFn: fetchDoctors, enabled: !!service });

  const open = (id: 'new' | string) => {
    const s = id === 'new' ? null : services.find(x => x.id === id);
    form.reset(s ? toServiceForm(s) : emptyService);
    priceForm.reset({ amount: '', effectiveOn: today });
    setPriceDoctor(null);
    setFormError(null);
    setPriceError(null);
    setEditing(id);
  };
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['billing', 'services'] });

  const onFailed = (e: unknown, setError: (c: CatalogueProblem) => void, fields: readonly string[], setField: (f: string, c: CatalogueProblem) => void) => {
    const { field, code } = problemOf(e);
    if (field && fields.includes(field)) setField(field, code);
    else setError(code);
  };

  const save = useMutation({
    mutationFn: (values: ServiceForm) => (service ? updateService(service.id, toServiceBody(values, service)) : createService(toServiceBody(values))),
    onSuccess: saved => { setFormError(null); void refresh(); setEditing(saved.id); form.reset(toServiceForm(saved)); },
    onError: e => onFailed(e, setFormError, ['name', 'category', 'code', 'sac', 'gstRateBasisPoints', 'durationMinutes'],
      (f, code) => form.setError((f === 'gstRateBasisPoints' ? 'gstRate' : f) as keyof ServiceForm, { type: 'server', message: code })),
  });
  const archive = useMutation({
    mutationFn: (s: Service) => archiveService(s.id, !s.archivedAt),
    onSuccess: () => { void refresh(); setEditing(null); },
    onError: e => setFormError(problemOf(e).code),
  });
  const price = useMutation({
    mutationFn: (values: PriceForm) => addPrice(service!.id, toPriceBody(priceDoctor, values)),
    onSuccess: () => {
      setNow(new Date());
      setPriceError(null);
      priceForm.reset({ amount: '', effectiveOn: today });
      void queryClient.invalidateQueries({ queryKey: ['billing', 'prices', service?.id] });
      void queryClient.invalidateQueries({ queryKey: ['settings', 'doctors'] }); // a consultation price is a doctor's fee on the Doctors tab
      void refresh();
    },
    onError: e => onFailed(e, setPriceError, ['amountMinor', 'effectiveOn'],
      (f, code) => priceForm.setError(f === 'amountMinor' ? 'amount' : 'effectiveOn', { type: 'server', message: code })),
  });

  const versions = prices.data ?? [];
  const rows: PriceRow[] = [
    { doctorId: null, name: null, location: null, current: priceFor(versions, null, now), upcoming: upcomingFor(versions, null, now) },
    ...(doctors.data?.doctors ?? []).filter(d => d.status !== 'Offboarded').map(d => {
      const own = priceFor(versions.filter(v => v.doctorId === d.id), d.id, now);
      return { doctorId: d.id, name: d.fullName, location: d.facilityName, current: own, upcoming: upcomingFor(versions, d.id, now) };
    }),
  ];

  return {
    archived, onArchived: setArchived,
    services, table, loading: list.isPending, failed: list.isError,
    // A service that left the list (archived while archived ones are hidden) closes its editor.
    editing: editing === 'new' || service ? editing : null, service, onOpen: open, onClose: () => setEditing(null),
    form, formError, saving: save.isPending, saved: save.isSuccess && !form.formState.isDirty, onSave: form.handleSubmit(values => save.mutate(values)),
    archiving: archive.isPending, onArchive: () => service && archive.mutate(service),
    rows, pricesLoading: prices.isPending && !!service, pricesFailed: prices.isError || doctors.isError,
    priceForm, priceDoctor, onPriceDoctor: setPriceDoctor, priceError, pricing: price.isPending, today,
    onAddPrice: priceForm.handleSubmit(values => price.mutate(values)),
  };
}
