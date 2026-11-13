import MyDevicesPage from '../features/my-devices/components/MyDevicesPage';
import { useMyDevicesController } from '../features/my-devices/hooks/useMyDevicesController';

/** Composition only: My devices (MDV-P0-012), for every signed-in staff member; times show in the facility's zone. */
export default function MyDevicesRoute({ timeZone }: { timeZone?: string }) {
  return <MyDevicesPage {...useMyDevicesController()} timeZone={timeZone} />;
}
