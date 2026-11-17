import { useI18n } from '@medvya/i18n';
import { Card } from '@medvya/ui';
import MyDevicesPage from '../features/my-devices/components/MyDevicesPage';
import { useMyDevicesController } from '../features/my-devices/hooks/useMyDevicesController';
import SignatureImage from '../features/settings/components/SignatureImage';
import { useMySignatureController } from '../features/settings/hooks/useMySignatureController';

/**
 * Composition only: My devices (MDV-P0-012), for every signed-in staff member; times show in the facility's zone. A prescriber also
 * keeps their own signature image here (#1365): it is the one page every account has, and practice settings are closed to an employed doctor.
 */
export default function MyDevicesRoute({ timeZone, prescriber }: { timeZone: string; prescriber: boolean }) {
  const { t } = useI18n();
  const signature = useMySignatureController(prescriber);
  return (
    <MyDevicesPage {...useMyDevicesController()} timeZone={timeZone}>
      {signature.hasSignature !== null && (
        <Card title={t('doctors.signature.mine')}>
          <SignatureImage id="mine" own {...signature} hasSignature={signature.hasSignature} />
        </Card>
      )}
    </MyDevicesPage>
  );
}
