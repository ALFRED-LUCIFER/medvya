from datetime import date

import pytest

from medvya_ai.services.analytics import bucket_start, medication_sql


@pytest.mark.parametrize(
    ("day", "group", "start"),
    [
        (date(2026, 11, 8), "day", date(2026, 11, 8)),
        (date(2026, 11, 8), "week", date(2026, 11, 2)),  # Sunday -> that week's Monday
        (date(2026, 11, 2), "week", date(2026, 11, 2)),  # Monday is its own start
        (date(2027, 1, 1), "week", date(2026, 12, 28)),  # week crossing the year boundary
        (date(2026, 11, 30), "month", date(2026, 11, 1)),
        (date(2028, 2, 29), "month", date(2028, 2, 1)),
    ],
)
def test_bucket_start(day: date, group: str, start: date) -> None:
    assert bucket_start(day, group) == start  # type: ignore[arg-type]


def test_medication_sql_reads_view_by_default_and_tables_for_drafts() -> None:
    approved, p1 = medication_sql("week", date(2026, 11, 1), None, include_draft=False)
    drafts, _ = medication_sql("week", None, date(2026, 11, 30), include_draft=True)
    assert "v_medication_usage" in approved and "date_trunc('week'" in approved
    assert p1 == {"d_from": date(2026, 11, 1)}
    assert "v_medication_usage" not in drafts
    assert "FROM medication m" in drafts and "'review', 'approved'" in drafts
    assert "rejected" in drafts  # rejected facts stay excluded in both modes
