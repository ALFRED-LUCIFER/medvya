import GlobalSearch from '../features/global-search/components/GlobalSearch';
import { useGlobalSearchController } from '../features/global-search/hooks/useGlobalSearchController';

type Props = { recordBase: string; startVisitPath: string | null; bookPath: string | null; registerPath: string | null };

/**
 * Composition only: the ⌘K search (D-02, MDV-P2-003) for roles that treat patients.
 * shortcut: "Open chart" opens the patient record until Patient 360 (MDV-P2-005+) exists; "Book" opens the schedule, where the patient is chosen again.
 */
export default function GlobalSearchRoute({ recordBase, startVisitPath, bookPath, registerPath }: Props) {
  return <GlobalSearch {...useGlobalSearchController(id => `${recordBase}/${id}`)} startVisitPath={startVisitPath} bookPath={bookPath} registerPath={registerPath} />;
}
