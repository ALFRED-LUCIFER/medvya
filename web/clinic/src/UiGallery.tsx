import { Box, Button, Stack, TextField, Typography } from '@mui/material';
import { AutoAwesome, Draw, StopCircle } from '@mui/icons-material';
import {
  alertLooks, ClinicalAlert, IdentityBanner, provenanceLooks, ProvenanceTag,
  RecordingPill, recordingLooks, StatusChip, statusLooks,
} from '@medvya/ui';

const keys = <T extends object>(o: T) => Object.keys(o) as (keyof T)[];

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <Box component="section" sx={{ display: 'flex', flexDirection: 'column', gap: 1.5 }}>
      <Typography variant="h2">{title}</Typography>
      <Stack direction="row" useFlexGap sx={{ flexWrap: 'wrap', gap: 1.5, alignItems: 'center' }}>{children}</Stack>
    </Box>
  );
}

// Every primitive in every state (MDV-P0-020). Synthetic data only.
// Dev-only page: its sample text is exempt from react/jsx-no-literals (.oxlintrc.json).
export default function UiGallery({ switcher }: { switcher: React.ReactNode }) {
  return (
    <Box component="main" sx={{ maxWidth: 1280, mx: 'auto', p: 3, display: 'flex', flexDirection: 'column', gap: 4 }}>
      <Stack direction="row" useFlexGap sx={{ gap: 2, alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap' }}>
        <Typography variant="h1">Medvya UI primitives</Typography>{switcher}
      </Stack>
      <Section title="Patient identity banner">
        <IdentityBanner patient={{ name: 'Meera Iyer', age: 54, sex: 'F', mrn: 'DQ-10-4482', dob: '12 Mar 1972',
          allergies: [{ name: 'Penicillin', status: 'verified' }], consent: 'given', encounter: 'Encounter 04 Oct 2026 · OPD Room 3', synthetic: true }} />
        <IdentityBanner patient={{ name: 'Deepak Nair', age: 47, sex: 'M', mrn: 'DQ-11-1093', dob: '14 Aug 1979', allergies: [], synthetic: true }} />
        <IdentityBanner patient={{ name: 'Fatima Al Mansoori', age: 38, sex: 'F', mrn: 'DQ-AE-0458', dob: '03 Feb 1988', allergies: null, consent: 'notAsked', synthetic: true }} />
      </Section>
      <Section title="Buttons">
        <Button variant="contained" startIcon={<Draw />}>Sign note</Button>
        <Button variant="contained" loading loadingPosition="start" startIcon={<Draw />}>Signing…</Button>
        <Button variant="contained" disabled>Sign note (2 unresolved)</Button>
        <Button variant="outlined">Save draft</Button>
        <Button variant="contained" color="ai" startIcon={<AutoAwesome />}>Generate draft</Button>
        <Button variant="contained" color="error" startIcon={<StopCircle />}>Stop recording</Button>
      </Section>
      <Section title="Closed-loop status">{keys(statusLooks).map(s => <StatusChip key={s} state={s} />)}</Section>
      <Section title="Provenance">{keys(provenanceLooks).map(s => <ProvenanceTag key={s} source={s} />)}</Section>
      <Section title="Recording and consent">{keys(recordingLooks).map(s => <RecordingPill key={s} state={s} elapsedSeconds={252} />)}</Section>
      <Section title="Alerts">
        {keys(alertLooks).map(s => <Box key={s} sx={{ minWidth: 320 }}><ClinicalAlert severity={s}>Synthetic example text</ClinicalAlert></Box>)}
      </Section>
      <Section title="Text fields">
        <TextField label="Full name" defaultValue="Deepak Nair" />
        <TextField label="PIN code" defaultValue="68203" error helperText="PIN code must be 6 digits" />
        <TextField label="Locked" defaultValue="Locked" disabled />
      </Section>
    </Box>
  );
}
