import { useState } from 'react';
import { useI18n } from '@medvya/i18n';
import { ToothChart, type ToothMark } from '@medvya/ui';

// ToothChart on /__ui (MDV-P8-011): the mixed dentition (permanent and primary rows), condition codes on teeth, unrecorded teeth and a selected one.
// Words come from the catalogue, so the Arabic gallery shows Arabic around a chart that stays left to right. Synthetic data only. No view switch and no
// Implant mark here: the gallery's other specs find "the" segmented control and the "Plan" label by text.
export default function ToothChartSection() {
  const { t } = useI18n();
  const [selected, setSelected] = useState<string | null>('36');
  type C = 'Sound' | 'Caries' | 'Filling' | 'Crown' | 'RootCanalTreated' | 'Missing' | 'Bridge' | 'Mobile' | 'Fractured';
  const mark = (c: C, tone: ToothMark['tone'], draw: Pick<ToothMark, 'surfaces' | 'fill' | 'symbol'> = {}): ToothMark =>
    ({ abbr: t(`dental.condition.abbr.${c}`), label: t(`dental.condition.${c}`), tone, ...draw });
  const marks: Record<string, ToothMark> = {
    '11': mark('Sound', 'success'), '16': mark('Crown', 'warning', { fill: 'dots' }), '26': mark('RootCanalTreated', 'info', { symbol: 'line' }),
    '36': mark('Caries', 'danger', { surfaces: 'DO', fill: 'hatch' }), '46': mark('Missing', 'neutral', { fill: 'faded', symbol: 'cross' }),
    '37': mark('Filling', 'info', { surfaces: 'MO', fill: 'solid' }), '24': mark('Bridge', 'warning', { symbol: 'bar' }), '31': mark('Mobile', 'warning', { symbol: 'arrows' }),
    '21': mark('Fractured', 'danger', { symbol: 'zigzag' }), '55': mark('Caries', 'danger', { surfaces: 'O', fill: 'hatch' }), '75': mark('Filling', 'info', { surfaces: 'B', fill: 'solid' }),
  };
  return (
    <section aria-labelledby="g-tooth-chart" className="flex flex-col gap-3">
      <h2 id="g-tooth-chart" className="text-heading">ToothChart (FDI, mixed dentition, facing the patient, surfaces drawn, codes always printed)</h2>
      <ToothChart dentition="mixed" marks={marks} notRecorded={t('dental.notRecorded')} selected={selected} onSelect={setSelected} />
    </section>
  );
}
