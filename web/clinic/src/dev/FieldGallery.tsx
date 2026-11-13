import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { Button, ErrorSummary, Field, FieldMarks, Input } from '@medvya/ui';

// MDV-P1-085 on /__ui: a sample form with React Hook Form and Zod. Submitted empty, three fields fail. Synthetic data only.
const schema = z.object({
  name: z.string().min(1, 'Enter the full name'),
  mobile: z.string().regex(/^\d{10}$/, 'Enter a 10-digit mobile number'),
  pin: z.string().regex(/^\d{6}$/, 'Enter a 6-digit PIN code'),
  email: z.string(),
  weight: z.string(),
});
type Values = z.infer<typeof schema>;

export default function FieldGallery() {
  const { register, handleSubmit, formState: { errors, submitCount } } = useForm<Values>({
    resolver: zodResolver(schema), shouldFocusError: false, defaultValues: { name: '', mobile: '', pin: '', email: '', weight: '' },
  });
  const list = (Object.keys(errors) as (keyof Values)[]).map(k => ({ id: `fg-${k}`, message: errors[k]?.message }));
  return (
    <section className="flex flex-col gap-3">
      <h2 className="text-lg font-bold">Field and ErrorSummary (sample form)</h2>
      <form noValidate onSubmit={handleSubmit(() => {})} className="flex max-w-form flex-col gap-4">
        <ErrorSummary title="There is a problem" errors={list} focusKey={submitCount} />
        <FieldMarks mark="required">
          <Field id="fg-name" label="Full name" required error={errors.name?.message}><Input {...register('name')} /></Field>
          <Field id="fg-mobile" label="Mobile number" required hint="10 digits, no country code" error={errors.mobile?.message}>
            <Input dir="ltr" inputMode="numeric" {...register('mobile')} />
          </Field>
          <Field id="fg-pin" label="PIN code" required error={errors.pin?.message}><Input dir="ltr" inputMode="numeric" {...register('pin')} /></Field>
          <Field id="fg-email" label="Email"><Input dir="ltr" type="email" {...register('email')} /></Field>
          <Field id="fg-weight" label="Weight" unit="kg"><Input dir="ltr" inputMode="decimal" className="w-24" {...register('weight')} /></Field>
        </FieldMarks>
        <Button type="submit" className="self-start">Save patient</Button>
      </form>
    </section>
  );
}
