import { Navigate, useNavigate, useParams } from 'react-router';
import { useI18n } from '@medvya/i18n';
import { PageContainer, PageHeader, PageMessage } from '@medvya/ui';
import DayClosePage from '../features/billing/components/DayClosePage';
import InvoiceListPage from '../features/billing/components/InvoiceListPage';
import InvoicePage from '../features/billing/components/InvoicePage';
import { useDayCloseController } from '../features/billing/hooks/useDayCloseController';
import { useInvoiceController } from '../features/billing/hooks/useInvoiceController';
import { useInvoicesController } from '../features/billing/hooks/useInvoicesController';
import { useVisitInvoiceController } from '../features/billing/hooks/useVisitInvoiceController';

/** Composition only (MDV-P4-004): the bills list, one bill, and the board's way into a visit's bill. */
export function BillingListRoute({ path, timeZone }: { path: string; timeZone: string }) {
  const navigate = useNavigate();
  return <InvoiceListPage {...useInvoicesController(id => void navigate(`${path}/${id}`))} timeZone={timeZone} />;
}

export function InvoiceRoute({ path, timeZone }: { path: string; timeZone: string }) {
  const { invoiceId = '' } = useParams();
  return <InvoicePage {...useInvoiceController(invoiceId)} timeZone={timeZone} listPath={path} />;
}

/** The board's Billing card: opens the visit's bill (a new draft with the suggested consultation the first time), then shows it. */
export function VisitInvoiceRoute({ path }: { path: string }) {
  const { t } = useI18n();
  const { appointmentId = '' } = useParams();
  const visit = useVisitInvoiceController(appointmentId);
  if (visit.invoiceId) return <Navigate to={`${path}/${visit.invoiceId}`} replace />;
  return (
    <PageContainer>
      <PageHeader title={t('billing.invoices.title')} />
      {visit.problem ? <PageMessage kind="error" title={t('billing.invoices.title')}>{t(`billing.error.${visit.problem}`)}</PageMessage>
        : <PageMessage kind="busy">{t('billing.invoice.opening')}</PageMessage>}
    </PageContainer>
  );
}

/** Day close (MDV-P4-012): the drawer count and the shift reports. */
export function DayCloseRoute({ timeZone }: { timeZone: string }) {
  return <DayClosePage {...useDayCloseController()} timeZone={timeZone} />;
}
