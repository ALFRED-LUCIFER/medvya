import { useState } from 'react';
import { useI18n } from '@medvya/i18n';
import { ToothChart } from '@medvya/ui';

// ToothChart on /__ui (MDV-P8-011): the mixed dentition (permanent and primary rows), condition codes on teeth, unrecorded teeth and a selected one.
// Words come from the catalogue, so the Arabic gallery shows Arabic around a chart that stays left to right. Synthetic data only. No view switch and no
// Implant mark here: the gallery's other specs find "the" segmented control and the "Plan" label by text.
export default function ToothChartSection() {
  const { t } = useI18n();
  const [selected, setSelected] = useState<string | null>('36');
  const mark = (c: 'Sound' | 'Caries' | 'Filling' | 'Crown' | 'RootCanalTreated' | 'Missing' | 'Bridge' | 'Mobile' | 'Fractured', tone: 'success' | 'danger' | 'info' | 'warning' | 'neutral') =>
    ({ abbr: t(`dental.condition.abbr.${c}`), label: t(`dental.condition.${c}`), tone });
  const marks = {
    '11': mark('Sound', 'success'), '16': mark('Crown', 'info'), '26': mark('RootCanalTreated', 'info'), '36': mark('Caries', 'danger'), '46': mark('Missing', 'neutral'),
    '37': mark('Filling', 'info'), '24': mark('Bridge', 'info'), '31': mark('Mobile', 'warning'), '21': mark('Fractured', 'danger'),
    '55': mark('Caries', 'danger'), '75': mark('Filling', 'info'),
  };
  return (
    <section aria-labelledby="g-tooth-chart" className="flex flex-col gap-3">
      <h2 id="g-tooth-chart" className="text-heading">ToothChart (FDI, mixed dentition, facing the patient, codes always printed)</h2>
      <ToothChart dentition="mixed" marks={marks} notRecorded={t('dental.notRecorded')} selected={selected} onSelect={setSelected} />
    </section>
  );
}
