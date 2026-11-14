import { useI18n } from '@medvya/i18n';
import { SegmentedControl } from '@medvya/ui';
import { frontDeskViews, type FrontDeskView } from '../domain/board';

/** The reception board's views (MDV audit UX-05, MDV-P2-052): visits by status, by doctor with the waiting room, or one list. One screen, one switch. */
export default function FrontDeskViews({ value, onValueChange }: { value: FrontDeskView; onValueChange: (view: FrontDeskView) => void }) {
  const { t } = useI18n();
  return (
    <SegmentedControl label={t('frontDesk.views')} value={value} onValueChange={onValueChange}
      // Words only: the header row has to fit at 1024 px (MDV-P1-095).
      options={frontDeskViews.map(v => ({ value: v, label: t(`frontDesk.view.${v}`) }))} />
  );
}
