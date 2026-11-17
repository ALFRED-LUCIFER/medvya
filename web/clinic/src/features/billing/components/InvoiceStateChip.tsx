import { Ban, CircleCheck, CircleDashed, FileText, Hourglass, type LucideIcon } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Chip } from '@medvya/ui';
import { stateLook, type InvoiceState } from '../domain/invoice';

const icons: Record<(typeof stateLook)[InvoiceState]['icon'], LucideIcon> = {
  draft: CircleDashed, issued: FileText, partly: Hourglass, paid: CircleCheck, cancelled: Ban,
};

/** A bill's state in words with an icon; the tone only repeats them (never colour alone). */
export default function InvoiceStateChip({ state }: { state: InvoiceState }) {
  const { t } = useI18n();
  return <Chip tone={stateLook[state].tone} icon={icons[stateLook[state].icon]} label={t(`billing.state.${state}`)} />;
}
