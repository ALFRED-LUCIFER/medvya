import { useI18n } from '@medvya/i18n';
import { SegmentedControl } from '@medvya/ui';

export type FrontDeskView = 'board' | 'doctors';

/** The reception board's two views (MDV audit UX-05): visits by status, or by doctor with the waiting room. One screen, one switch. */
export default function FrontDeskViews({ value, onValueChange }: { value: FrontDeskView; onValueChange: (view: FrontDeskView) => void }) {
  const { t } = useI18n();
  return (
    <SegmentedControl label={t('frontDesk.views')} value={value} onValueChange={onValueChange}
      // Words only: the header row has to fit at 1024 px (MDV-P1-095).
      options={[{ value: 'board', label: t('frontDesk.view.board') }, { value: 'doctors', label: t('frontDesk.view.doctors') }]} />
  );
}
