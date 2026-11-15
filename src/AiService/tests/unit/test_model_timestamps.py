import sqlalchemy as sa

from medvya_ai.models.base import Base


def test_every_datetime_column_is_timezone_aware() -> None:
    naive = [
        f"{t.name}.{c.name}"
        for t in Base.metadata.tables.values()
        for c in t.columns
        if isinstance(c.type, sa.DateTime) and not c.type.timezone
    ]
    assert naive == []
