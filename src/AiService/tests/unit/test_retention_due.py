import uuid
from datetime import UTC, datetime, timedelta

from medvya_ai.services.retention import Policy, SessionAge, due

NOW = datetime(2026, 11, 8, 12, 0, tzinfo=UTC)
POLICY = Policy(audio_after_approval_hours=24, draft_audio_days=7)


def _s(state: str, *, created: timedelta = timedelta(0), approved: timedelta | None = None):  # type: ignore[no-untyped-def]
    return SessionAge(
        uuid.uuid4(), state, NOW - created, None if approved is None else NOW - approved
    )


def test_approved_boundary_is_24_hours_inclusive() -> None:
    just_before = _s("approved", approved=timedelta(hours=24) - timedelta(seconds=1))
    exactly = _s("approved", approved=timedelta(hours=24))
    after = _s("approved", approved=timedelta(hours=25))
    assert due([just_before, exactly, after], NOW, POLICY) == [exactly.id, after.id]


def test_draft_boundary_is_7_days_inclusive() -> None:
    young = _s("review", created=timedelta(days=7) - timedelta(seconds=1))
    old = _s("review", created=timedelta(days=7))
    abandoned = _s("recording", created=timedelta(days=30))
    assert due([young, old, abandoned], NOW, POLICY) == [old.id, abandoned.id]


def test_old_approved_session_age_is_measured_from_approval_not_creation() -> None:
    s = _s("approved", created=timedelta(days=60), approved=timedelta(hours=1))
    assert due([s], NOW, POLICY) == []


def test_per_clinic_policy_changes_the_boundary() -> None:
    s = _s("approved", approved=timedelta(hours=2))
    assert due([s], NOW, POLICY) == []
    assert due([s], NOW, Policy(1, 7)) == [s.id]
