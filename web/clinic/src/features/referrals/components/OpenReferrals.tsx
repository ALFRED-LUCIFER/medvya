import { useI18n } from '@medvya/i18n';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@medvya/ui';
import { Section } from '../../settings/components/Section';
import { openReferralColumns, type Referral } from '../domain/referrals';
import { stateIcons } from './stateIcons';

/** Open referrals on Patient 360 (E-01): who the patient was sent to, or came from, and where each stands. Read only. */
export default function OpenReferrals({ open }: { open: Referral[] }) {
  const { t } = useI18n();
  if (open.length === 0) return null;
  return (
    <Section title={t('referrals.open.title')}>
      <Table label={t('referrals.open.tableLabel')} className="min-w-[480px]">
        <TableHeader><TableRow>{openReferralColumns.map(c => <TableHead key={c} scope="col">{t(`referrals.col.${c}`)}</TableHead>)}</TableRow></TableHeader>
        <TableBody>
          {open.map(r => {
            const Icon = stateIcons[r.state];
            return (
              <TableRow key={r.id}>
                <TableHead scope="row">{r.provider}</TableHead>
                <TableCell>{t(`referrals.urgency.${r.urgency}`)}</TableCell>
                <TableCell><span className="inline-flex items-center gap-1.5 font-bold"><Icon aria-hidden className="size-4 shrink-0" />{t(`referrals.state.${r.state}`)}</span></TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
    </Section>
  );
}
