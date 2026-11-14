import { useState } from 'react';
import { LayoutGrid, List, Stethoscope } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, Card, EmptyState, PageMessage, SegmentedControl, Section, Skeleton, SkeletonGroup, SplitLayout } from '@medvya/ui';

// MDV-P1-086 on /__ui, with the Skeleton (MDV-P2-056). Sentences reuse existing catalogue keys so the Arabic gallery shows Arabic. Synthetic data only.
export default function PageComponentsGallery() {
  const { t } = useI18n();
  const [view, setView] = useState<'status' | 'doctor' | 'list'>('status');
  const [chartOpen, setChartOpen] = useState(false);
  return (
    <Section title="Page components (Card, PageMessage, Skeleton, SegmentedControl, SplitLayout)">
      <div className="grid gap-4 wide:grid-cols-2">
        <Card title="Card · section" intro="16 px padding, 12 px radius" actions={<Button variant="outline">Edit</Button>}>
          <Section title="Section inside a card" intro="No second border">Deepak Nair · A-07</Section>
        </Card>
        <div className="flex flex-col gap-2">
          <Card size="item" title="Card · item">Meera Iyer · A-08</Card>
          <Card size="item" title="Card · item">Fatima Al Mansoori · A-09</Card>
        </div>
      </div>
      <div className="grid gap-4 wide:grid-cols-2">
        <Card size="item"><PageMessage kind="busy">{t('board.loading')}</PageMessage></Card>
        <Card size="item"><PageMessage kind="error" title={t('patients.error.title')} action={<Button variant="outline">{t('schedule.retry')}</Button>}>{t('patients.error.body')}</PageMessage></Card>
        <Card size="item"><EmptyState>{t('board.empty.body')}</EmptyState></Card>
        <Card size="item"><PageMessage kind="denied" action={<Button variant="outline">{t('session.signOut')}</Button>}>{t('state.removed.body')}</PageMessage></Card>
      </div>
      {/* Shown after 200 ms in the final layout; pulses where motion is allowed, static blocks under reduced motion. */}
      <SkeletonGroup label={t('board.loading')} data-testid="skeleton" className="flex flex-col gap-2">
        <Skeleton className="h-5 w-1/3" />
        <Skeleton className="h-24 rounded-xl" />
      </SkeletonGroup>
      <SegmentedControl label="View" value={view} onValueChange={setView} options={[
        { value: 'status', label: t('frontDesk.view.board'), icon: LayoutGrid },
        { value: 'doctor', label: t('frontDesk.view.doctors'), icon: Stethoscope },
        { value: 'list', label: t('schedule.view.list'), icon: List },
      ]} />
      <SplitLayout data-testid="split" sideLabel={t('studio.panel.chart')} closeLabel={t('appointment.close')} open={chartOpen} onOpenChange={setChartOpen}
        side={<Card size="item" title="Allergies">Penicillin (rash)</Card>}>
        <Card title="Main pane" intro="The side pane is beside this at 1024 px and wider, and a sheet below.">
          <Button variant="outline" className="wide:hidden" onClick={() => setChartOpen(true)}>{t('studio.panel.chart')}</Button>
        </Card>
      </SplitLayout>
    </Section>
  );
}
