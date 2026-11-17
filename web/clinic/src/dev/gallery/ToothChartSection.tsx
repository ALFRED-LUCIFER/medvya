import { useState } from 'react';
import { useI18n } from '@medvya/i18n';
import { SegmentedControl, ToothChart, type Dentition } from '@medvya/ui';

// ToothChart on /__ui (MDV-P8-011): every condition's code on a tooth, an unrecorded tooth, a selected one, three dentitions. Words come from the
// catalogue, so the Arabic gallery shows Arabic around a chart that stays left to right. Synthetic data only.
export default function ToothChartSection() {
  const { t } = useI18n();
  const [dentition, setDentition] = useState<Dentition>('permanent');
  const [selected, setSelected] = useState<string | null>('36');
  const mark = (c: 'Sound' | 'Caries' | 'Filling' | 'Crown' | 'RootCanalTreated' | 'Missing' | 'Implant' | 'Bridge' | 'Mobile' | 'Fractured', tone: 'success' | 'danger' | 'info' | 'warning' | 'neutral') =>
    ({ abbr: t(`dental.condition.abbr.${c}`), label: t(`dental.condition.${c}`), tone });
  const marks = {
    '11': mark('Sound', 'success'), '16': mark('Crown', 'info'), '26': mark('RootCanalTreated', 'info'), '36': mark('Caries', 'danger'), '46': mark('Missing', 'neutral'),
    '37': mark('Filling', 'info'), '45': mark('Implant', 'info'), '24': mark('Bridge', 'info'), '31': mark('Mobile', 'warning'), '21': mark('Fractured', 'danger'),
    '55': mark('Caries', 'danger'), '75': mark('Filling', 'info'),
  };
  return (
    <section aria-labelledby="g-tooth-chart" className="flex flex-col gap-3">
      <h2 id="g-tooth-chart" className="text-heading">ToothChart (FDI, facing the patient, codes always printed)</h2>
      <SegmentedControl label={t('dental.chart.dentition')} value={dentition} onValueChange={setDentition}
        options={(['permanent', 'primary', 'mixed'] as const).map(d => ({ value: d, label: t(`dental.dentition.${d}`) }))} />
      <ToothChart dentition={dentition} marks={marks} notRecorded={t('dental.notRecorded')} selected={selected} onSelect={setSelected} />
    </section>
  );
}
