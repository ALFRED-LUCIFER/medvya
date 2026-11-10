import type { ReactNode } from 'react';
import { useParams } from 'react-router';
import RxVerifyPage from '../features/prescription/components/RxVerifyPage';
import SharedRxPage from '../features/prescription/components/SharedRxPage';
import { useRxVerifyController } from '../features/prescription/hooks/useRxPrintController';
import { useSharedRxController } from '../features/prescription/hooks/useRxShareController';

/** Composition only: the public QR check of a printed prescription (MDV-P2-017), the token from the address. */
export default function RxVerifyRoute({ switcher }: { switcher: ReactNode }) {
  return <RxVerifyPage {...useRxVerifyController(useParams().token ?? '')} languageSwitcher={switcher} />;
}

/** Composition only: a shared prescription link (MDV-P2-020), the token from the address. */
export function SharedRxRoute({ switcher }: { switcher: ReactNode }) {
  return <SharedRxPage {...useSharedRxController(useParams().token ?? '')} languageSwitcher={switcher} />;
}
