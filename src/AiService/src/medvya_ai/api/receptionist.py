import uuid

from fastapi import APIRouter

from medvya_ai.deps import LLMDep, TenantDep, WrapperDep
from medvya_ai.schemas import ConversationOut, ReceptionistMessageIn
from medvya_ai.services import receptionist

router = APIRouter(prefix="/receptionist/conversations", tags=["receptionist"])


@router.post("", response_model=ConversationOut, status_code=201)
async def start(t: TenantDep) -> ConversationOut:
    c = await receptionist.start(t.db, t.clinic_id)
    await t.audit("receptionist_start", "receptionist_conversation", c.id)
    return ConversationOut.model_validate(c)


@router.get("/{conversation_id}", response_model=ConversationOut)
async def get(conversation_id: uuid.UUID, t: TenantDep) -> ConversationOut:
    return ConversationOut.model_validate(
        await receptionist.get_conversation(t.db, conversation_id)
    )


@router.post("/{conversation_id}/messages", response_model=ConversationOut)
async def message(
    conversation_id: uuid.UUID,
    body: ReceptionistMessageIn,
    t: TenantDep,
    llm: LLMDep,
    wrapper: WrapperDep,
) -> ConversationOut:
    c = await receptionist.reply(
        t.db, llm, conversation_id, body.text, doctor_id=t.user_id, wrapper=wrapper
    )
    await t.audit(
        "receptionist_message",
        "receptionist_conversation",
        c.id,
        {"messages": len(c.messages), "status": c.status, "session_id": str(c.session_id or "")},
    )
    return ConversationOut.model_validate(c)
