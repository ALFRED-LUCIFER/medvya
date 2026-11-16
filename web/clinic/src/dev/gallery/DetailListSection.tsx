import { useI18n } from '@medvya/i18n';
import { Button, Card, Detail, DetailList } from '@medvya/ui';

// DetailList on /__ui: the four layouts. Labels reuse catalogue keys so the Arabic gallery shows Arabic. Synthetic data only.
export default function DetailListSection() {
  const { t, formatNumber } = useI18n();
  return (
    <section aria-labelledby="g-details" className="flex flex-col gap-3">
      <h2 id="g-details" className="text-heading">DetailList (stacked, inline, rows, tiles)</h2>
      <div className="grid gap-4 wide:grid-cols-2">
        <Card title="stacked">
          <DetailList>
            <Detail label={t('patients.record.dob')}>12 Mar 1972</Detail>
            <Detail label={t('patients.record.mobile')}><bdi dir="ltr">+91 98400 12345</bdi></Detail>
          </DetailList>
        </Card>
        <Card title="inline">
          <DetailList layout="inline">
            <Detail label={t('appointment.when')}>19 Oct · 10:00</Detail>
            <Detail label={t('appointment.type')}>{t('visitType.FollowUp')}</Detail>
          </DetailList>
        </Card>
      </div>
      <DetailList layout="rows">
        <Detail label={t('patients.record.dob')} action={<Button type="button" variant="ghost">{t('registration.review.editShort')}</Button>}>12 Mar 1972</Detail>
        <Detail label={t('settings.data.country')} action={<Button type="button" variant="ghost">{t('registration.review.editShort')}</Button>}>{t('settings.data.country.IN')}</Detail>
      </DetailList>
      <DetailList layout="tiles">
        <Detail label={t('clinicToday.booked')} note={t('clinicToday.toArrive', { count: formatNumber(4) })}>{formatNumber(18)}</Detail>
        <Detail label={t('clinicToday.waiting')}>{formatNumber(3)}</Detail>
      </DetailList>
    </section>
  );
}
