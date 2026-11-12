import { useState } from 'react';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm, type FieldErrors } from 'react-hook-form';
import { ApiError } from '../../schedule/services/scheduleApi';
import {
  doctorProblem, doctorSchema, doctorSteps, emptyDoctor, emptyLicence, errorTarget, fieldProblem, licenceSchema, signatureProblem, stepFields,
  type Doctor, type DoctorForm, type DoctorStep, type LicenceForm,
} from '../domain/doctors';
import { addDoctor, fetchDoctors, removeSignature, resubmitLicence, uploadSignature } from '../services/doctorsApi';

/** The invitation email could not be sent; the server saved nothing (502 with this code). */
const emailFailed = 'email_failed', planFull = 'plan_limit';

/** What to say under a field: a code the screen translates ("invalid" for what the form itself rejects, else the server's code). */
const codeOf = (e: FieldErrors<DoctorForm>[keyof DoctorForm]) => (e ? fieldProblem(e.type === 'server' ? String(e.message) : undefined) : undefined);

/** Doctors tab (F-09): the list, the Add doctor wizard, licence resubmission and the signature image. The server decides who may do what. */
export function useDoctorsController(defaultFacilityId: string) {
  const queryClient = useQueryClient();
  const list = useQuery({ queryKey: ['settings', 'doctors'], queryFn: fetchDoctors });
  // A new or changed doctor also changes the bookable calendars the Hours tab reads (cached for a minute, MDV audit UX-12).
  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['settings'] });

  // ---- Add doctor wizard ----
  const [adding, setAdding] = useState(false);
  const [step, setStep] = useState<DoctorStep>('details');
  const [signature, setSignature] = useState<File | null>(null);
  const [signatureProblemKind, setSignatureProblemKind] = useState<'type' | 'size' | null>(null);
  const [added, setAdded] = useState<{ name: string; signatureFailed: boolean } | null>(null);
  const form = useForm<DoctorForm>({ resolver: zodResolver(doctorSchema), defaultValues: emptyDoctor(defaultFacilityId) });

  const open = () => {
    form.reset(emptyDoctor(defaultFacilityId, list.data?.defaults));
    setStep('details');
    setSignature(null);
    setSignatureProblemKind(null);
    setAdded(null);
    setAdding(true);
  };
  const close = () => setAdding(false);

  const add = useMutation({
    mutationFn: async (values: DoctorForm) => {
      const doctor = await addDoctor(values);
      // The doctor exists from here on; a failed image upload must not make the whole add look failed.
      const signatureFailed = signature ? await uploadSignature(doctor.id, signature).then(() => false, () => true) : false;
      return { doctor, signatureFailed };
    },
    onSuccess: ({ doctor, signatureFailed }) => { setAdding(false); setAdded({ name: doctor.fullName, signatureFailed }); refresh(); },
    onError: e => {
      // "licence.number:format" → under that field, on its step.
      const [path, code] = (e instanceof ApiError ? e.code ?? '' : '').split(':');
      const target = errorTarget(path ?? '');
      if (target && code) { form.setError(target.field, { type: 'server', message: code }); setStep(target.step); }
    },
  });

  const index = doctorSteps.indexOf(step);
  const next = async () => {
    if (await form.trigger(stepFields[step])) setStep(doctorSteps[index + 1] ?? step);
  };
  const lastStep = index === doctorSteps.length - 1;

  const errors = form.formState.errors;
  const fieldErrors = Object.fromEntries(Object.entries(errors).map(([k, v]) => [k, codeOf(v as FieldErrors<DoctorForm>[keyof DoctorForm])]));

  // ---- Correct a rejected licence ----
  const [correcting, setCorrecting] = useState<Doctor | null>(null);
  const licenceForm = useForm<LicenceForm>({ resolver: zodResolver(licenceSchema), defaultValues: emptyLicence });
  const correct = useMutation({ mutationFn: (values: LicenceForm) => resubmitLicence(correcting!.id, values), onSuccess: () => { setCorrecting(null); refresh(); } });
  const licenceErrors = Object.fromEntries(Object.entries(licenceForm.formState.errors).map(([k, v]) => [k, v ? fieldProblem(undefined) : undefined]));

  // ---- Signature image of a listed doctor ----
  const [rowProblem, setRowProblem] = useState<{ id: string; kind: 'type' | 'size' | 'failed' } | null>(null);
  const afterSignature = () => { setRowProblem(null); refresh(); };
  const upload = useMutation({
    mutationFn: ({ id, file }: { id: string; file: File }) => uploadSignature(id, file),
    onSuccess: afterSignature,
    onError: (e, { id }) => {
      const code = e instanceof ApiError ? doctorProblem(e.code) : 'failed';
      setRowProblem({ id, kind: code === 'signature:type' ? 'type' : code === 'signature:too_large' ? 'size' : 'failed' });
    },
  });
  const remove = useMutation({ mutationFn: removeSignature, onSuccess: afterSignature, onError: (_e, id) => setRowProblem({ id, kind: 'failed' }) });

  return {
    loading: list.isPending,
    forbidden: list.error instanceof ApiError && list.error.status === 403,
    loadFailed: list.isError,
    doctors: list.data?.doctors ?? [],
    authorities: list.data?.authorities ?? [],
    currency: list.data?.defaults.currency ?? 'INR',
    onRetry: () => void list.refetch(),
    added,
    onDismissAdded: () => setAdded(null),

    adding,
    onOpen: open,
    onClose: close,
    step,
    stepIndex: index,
    lastStep,
    form,
    fieldErrors,
    onBack: () => setStep(doctorSteps[Math.max(0, index - 1)] ?? step),
    onNext: next,
    onSubmit: form.handleSubmit(values => add.mutate(values)),
    submitting: add.isPending,
    submitFailed: add.isError && !(add.error instanceof ApiError && (add.error.status === 400 || add.error.code === emailFailed || add.error.code === planFull)),
    planFull: add.error instanceof ApiError && add.error.code === planFull,
    emailFailed: add.error instanceof ApiError && add.error.code === emailFailed,
    signature,
    signatureProblem: signatureProblemKind,
    onPickSignature: (file: File | undefined) => {
      const problem = file ? signatureProblem(file) : null;
      setSignatureProblemKind(problem);
      setSignature(file && !problem ? file : null);
    },

    correcting,
    licenceForm,
    licenceErrors,
    onCorrect: (d: Doctor | null) => {
      setCorrecting(d);
      if (d) licenceForm.reset({ authority: d.authority, number: d.number, category: d.category ?? '', expiresOn: d.expiresOn ?? '', certificateName: '', attested: false as unknown as true });
    },
    onSubmitLicence: licenceForm.handleSubmit(values => correct.mutate(values)),
    correctingBusy: correct.isPending,
    correctFailed: correct.isError,

    rowProblem,
    busyId: upload.isPending ? upload.variables.id : remove.isPending ? remove.variables : null,
    onSignature: (id: string, file: File | undefined) => {
      if (!file) return;
      const problem = signatureProblem(file);
      if (problem) setRowProblem({ id, kind: problem });
      else upload.mutate({ id, file });
    },
    onRemoveSignature: (id: string) => remove.mutate(id),
  };
}
