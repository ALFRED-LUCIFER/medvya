import { MonitorSmartphone } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { PageContainer, PageHeader, PageMessage } from '@medvya/ui';

type Props = {
  /** The open patient, so the doctor knows whose record waits on the bigger screen. */
  patientName?: string | null;
  /** When the message replaces a whole page: that page's title, its one h1. */
  title?: string;
};

/**
 * The phone rule (ruling 5, MDV-P1-098): under 768 px a clinical write shows this in place of its controls. A usability rule in the client,
 * not a security control: the server's authorisation is unchanged.
 */
export default function OpenOnTablet({ patientName, title }: Props) {
  const { t } = useI18n();
  const message = (
    <PageMessage kind="empty" icon={MonitorSmartphone} data-phone-rule="">
      {patientName && <span className="block text-subheading">{patientName}</span>}
      {t('phoneRule.message')}
    </PageMessage>
  );
  return title ? <PageContainer width="form"><PageHeader title={title} />{message}</PageContainer> : message;
}
