import { useI18n } from '@medvya/i18n';
import { Button, PageContainer, PageHeader, Table, TableBody, TableCell, TableHead, TableHeader, TableRow, TableState } from '@medvya/ui';
import { fieldError } from '../../settings/components/fieldStyles';
import { canEnd, type DeviceSession } from '../domain/devices';

type Props = {
  devices: DeviceSession[]; loading: boolean; failed: boolean; endingId: string | null; endFailed: boolean; ended: boolean;
  timeZone?: string; onEnd: (id: string) => void;
};

/** My devices (MDV-P0-012): where this account is signed in, and a way to end any other session, e.g. a shared desk PC left signed in. */
export default function MyDevicesPage(p: Props) {
  const { t, formatDate, formatTime } = useI18n();
  const when = (iso: string) => `${formatDate(new Date(iso), p.timeZone)} ${formatTime(new Date(iso), p.timeZone)}`;
  const state = p.loading ? 'loading' : p.failed ? 'error' : p.devices.length === 0 ? 'empty' : 'ready';
  return (
    <PageContainer width="reading">
      <PageHeader title={t('myDevices.title')} />
      <p className="text-muted-foreground">{t('myDevices.intro')}</p>
      <div aria-live="polite" className="text-sm font-semibold text-success-700">{p.ended && !p.endFailed && t('myDevices.ended')}</div>
      {p.endFailed && <p role="alert" className={fieldError}>{t('myDevices.endFailed')}</p>}
      <TableState state={state} messages={{ loading: t('settings.loading'), error: t('settings.loadFailed'), empty: t('myDevices.empty') }}>
        <Table label={t('myDevices.tableLabel')} className="min-w-[560px]">
          <TableHeader>
            <TableRow>
              <TableHead scope="col">{t('myDevices.col.device')}</TableHead>
              <TableHead scope="col">{t('myDevices.col.signedIn')}</TableHead>
              <TableHead scope="col">{t('myDevices.col.lastActive')}</TableHead>
              <TableHead scope="col"><span className="sr-only">{t('myDevices.col.actions')}</span></TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {p.devices.map(d => (
              <TableRow key={d.id}>
                <TableHead scope="row">{d.device}{d.current && <span className="block text-sm font-bold text-muted-foreground">{t('myDevices.thisDevice')}</span>}</TableHead>
                <TableCell>{when(d.createdAt)}</TableCell>
                <TableCell>{when(d.lastSeenAt)}</TableCell>
                <TableCell>
                  {canEnd(d) && (
                    <Button type="button" variant="outline" className="min-h-11 min-w-11" loading={p.endingId === d.id} disabled={p.endingId !== null}
                      aria-label={t('myDevices.endNamed', { device: d.device, date: when(d.lastSeenAt) })} onClick={() => p.onEnd(d.id)}>{t('myDevices.end')}</Button>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableState>
    </PageContainer>
  );
}
