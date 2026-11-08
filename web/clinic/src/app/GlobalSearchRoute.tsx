import GlobalSearch from '../features/global-search/components/GlobalSearch';
import { useGlobalSearchController } from '../features/global-search/hooks/useGlobalSearchController';

type Props = { startVisitPath: string | null; bookPath: string | null; registerPath: string | null };

/**
 * Composition only: the ⌘K search (D-02, MDV-P2-003) for roles that treat patients.
 * "Open chart" opens Patient 360, where the server checks the care relationship; "Book" opens the schedule with the patient already chosen.
 */
export default function GlobalSearchRoute({ startVisitPath, bookPath, registerPath }: Props) {
  return <GlobalSearch {...useGlobalSearchController(id => `/patients/${id}`)} startVisitPath={startVisitPath} bookPath={bookPath} registerPath={registerPath} />;
}
