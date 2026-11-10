import type { ReactNode } from 'react';
import { useParams } from 'react-router';
import RxVerifyPage from '../features/prescription/components/RxVerifyPage';
import { useRxVerifyController } from '../features/prescription/hooks/useRxPrintController';

/** Composition only: the public QR check of a printed prescription (MDV-P2-017), the token from the address. */
export default function RxVerifyRoute({ switcher }: { switcher: ReactNode }) {
  return <RxVerifyPage {...useRxVerifyController(useParams().token ?? '')} languageSwitcher={switcher} />;
}
