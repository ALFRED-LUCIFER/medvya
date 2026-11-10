import OrganisationRequestPage from '../features/onboarding/components/OrganisationRequestPage';
import { useOrganisationRequestController } from '../features/onboarding/hooks/useOrganisationRequestController';

/** F-04: the owner's onboarding request: status, documents and Submit (ADR-016). The server decides who may see it. */
export default function OrganisationRoute() {
  return <OrganisationRequestPage {...useOrganisationRequestController()} />;
}
