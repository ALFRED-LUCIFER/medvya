import { useState } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { useI18n } from '@medvya/i18n';
import { usePatientSearch } from '../../patients/hooks/usePatientSearch';
import { ApiError } from '../../api/services/apiClient';
import { useTableController } from '../../table/hooks/useTableController';
import { invoiceProblem, invoiceSort, type InvoiceState } from '../domain/invoice';
import { createInvoice, searchInvoices } from '../services/invoicesApi';

/**
 * /billing (MDV-P4-004): the facility's bills, newest first, filtered by state, a day range and a patient chosen with front-desk search (the search text
 * goes in a POST body, never a URL). With a patient chosen the desk can start a new bill for them.
 */
export function useInvoicesController(onOpen: (id: string) => void) {
  const { t } = useI18n();
  const [state, setState] = useState<InvoiceState | null>(null);
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [patientId, setPatientId] = useState<string | null>(null);
  const search = usePatientSearch(id => setPatientId(id));
  const filters = { state, from: from || null, to: to || null, patientId };
  const list = useQuery({ queryKey: ['billing', 'invoices', filters], queryFn: () => searchInvoices(filters) });
  const rows = list.data ?? [];
  const table = useTableController(rows, invoiceSort, c => t(`billing.invoices.col.${c}`));
  const create = useMutation({ mutationFn: (id: string) => createInvoice(id), onSuccess: bill => onOpen(bill.id) });

  return {
    state, onState: setState, from, onFrom: setFrom, to, onTo: setTo,
    patientId, patientName: patientId ? (rows.find(r => r.patientId === patientId)?.patientName ?? null) : null,
    patientSearch: search.view, onClearPatient: () => { setPatientId(null); search.clear(); },
    table, empty: rows.length === 0,
    status: list.isPending ? ('loading' as const) : list.error instanceof ApiError && list.error.status === 403 ? ('forbidden' as const) : list.isError ? ('error' as const) : ('ready' as const),
    creating: create.isPending, createError: create.isError ? invoiceProblem(create.error instanceof ApiError ? create.error.code : undefined) : null,
    onCreate: () => patientId && create.mutate(patientId),
    onOpen,
  };
}
