import { useParams } from 'react-router';
import ConsultPage from '../features/encounters/components/ConsultPage';
import NoteEditor from '../features/encounters/components/NoteEditor';
import NoteTemplateChoice from '../features/encounters/components/NoteTemplateChoice';
import { useConsultController } from '../features/encounters/hooks/useConsultController';
import { useNoteController } from '../features/encounters/hooks/useNoteController';
import { useNoteTemplatesController } from '../features/encounters/hooks/useNoteTemplatesController';

/** Composition only: one consultation (MDV-P2-001) by its id in the address, with its note (MDV-P2-009); the server checks clinical access on every call. */
export default function ConsultRoute({ boardPath }: { boardPath: string }) {
  const { encounterId = '' } = useParams();
  return <Consult key={encounterId} encounterId={encounterId} boardPath={boardPath} />;
}

function Consult({ encounterId, boardPath }: { encounterId: string; boardPath: string }) {
  return (
    <ConsultPage {...useConsultController(encounterId)} boardPath={boardPath}>
      <NoteEditor {...useNoteController(encounterId)} />
      <NoteTemplateChoice {...useNoteTemplatesController()} />
    </ConsultPage>
  );
}
