"""AC-level guard for the participant cap, which has no DB constraint (migration review)."""

import uuid
from types import SimpleNamespace
from typing import Any

import pytest

from medvya_ai.errors import AppError
from medvya_ai.services.consent import add_participant


class _Db:
    def __init__(self, existing: int) -> None:
        self.n = existing

    async def scalars(self, _: Any) -> Any:
        return SimpleNamespace(all=lambda: [uuid.uuid4() for _ in range(self.n)])


async def test_fourth_participant_is_rejected() -> None:
    s = SimpleNamespace(id=uuid.uuid4(), clinic_id=uuid.uuid4(), state="created")
    with pytest.raises(AppError) as exc:
        await add_participant(_Db(3), s, "companion", "X")  # type: ignore[arg-type]
    assert exc.value.status == 409 and exc.value.code == "PARTICIPANT_LIMIT"
