import { useNavigate } from 'react-router';
import ClinicOverviewPage from '../features/front-desk/components/ClinicOverviewPage';
import FrontDeskViews from '../features/front-desk/components/FrontDeskViews';
import { useOverviewController } from '../features/front-desk/hooks/useOverviewController';
import { useQueueTableController } from '../features/front-desk/hooks/useQueueTableController';

type Props = { facilityId: string; online: boolean; canMove: boolean; userId: string; boardPath: string };

/** Composition only: the board's "by doctor" view (D-04 clinic overview, MDV-P1-030) of the active facility. */
export default function OverviewRoute({ facilityId, online, canMove, userId, boardPath }: Props) {
  const navigate = useNavigate();
  const overview = useOverviewController(facilityId, online, userId);
  const names = new Map(overview.overview?.doctors.map(d => [d.calendarId, d.name]));
  const waiting = useQueueTableController((overview.overview?.waiting ?? []).map(w => ({ ...w, doctor: names.get(w.calendarId) ?? '' })));
  return <ClinicOverviewPage {...overview} canMove={canMove} waiting={waiting} views={<FrontDeskViews value="doctors" onValueChange={v => { if (v === 'board') void navigate(boardPath); }} />} />;
}
