"""Offline (no database) checks of the SQL that migration 0001 generates."""

import io
import re
from pathlib import Path

import pytest
from alembic import command
from alembic.config import Config

from medvya_ai.models import Base
from medvya_ai.models.base import ENUMS

API_DIR = Path(__file__).resolve().parents[2]


@pytest.fixture(scope="module")
def sql() -> str:
    buf = io.StringIO()
    cfg = Config(str(API_DIR / "alembic.ini"), output_buffer=buf)
    command.upgrade(cfg, "head", sql=True)
    return buf.getvalue()


def test_enums_match_models(sql: str) -> None:
    """CREATE TYPE from 0001 plus every ALTER TYPE ... ADD VALUE must equal the model enums."""
    for name, values in ENUMS.items():
        m = re.search(rf"CREATE TYPE {name} AS ENUM \(([^)]*)\)", sql)
        assert m, name
        created = [v.strip("' ") for v in m.group(1).split(",")]
        added = re.findall(rf"ALTER TYPE {name} ADD VALUE IF NOT EXISTS '(\w+)'", sql)
        assert created + added == list(values), name


def test_every_model_table_created(sql: str) -> None:
    for table in Base.metadata.tables:
        assert re.search(rf"CREATE TABLE {table} \(", sql), table


def test_rls_forced_with_missing_ok_policy(sql: str) -> None:
    forced = set(re.findall(r"ALTER TABLE (\w+) FORCE ROW LEVEL SECURITY", sql))
    assert len(forced) == 18 and "clinic" not in forced and "processing_job" not in forced
    assert "current_setting('app.clinic_id', true)" in sql and "NULLIF" in sql
    assert sql.count("WITH CHECK (clinic_id = app_clinic_id())") == 18
    assert "BYPASSRLS" not in sql.replace("NOBYPASSRLS", "").replace("BYPASSRLS (RLS", "")


def test_append_only_triggers_and_revoke(sql: str) -> None:
    for t in ("consent_record", "note_revision", "audit_event"):
        assert f"BEFORE UPDATE OR DELETE ON {t}" in sql
        assert f"BEFORE TRUNCATE ON {t}" in sql
    assert "REVOKE UPDATE, DELETE, TRUNCATE ON audit_event FROM medvya_ai_app" in sql


def test_views_security_invoker(sql: str) -> None:
    for v in ("v_current_consent", "v_medication_usage", "v_fact_counts"):
        assert re.search(rf"CREATE VIEW {v} WITH \(security_invoker = true\)", sql)


def test_downgrade_sql_generates() -> None:
    buf = io.StringIO()
    cfg = Config(str(API_DIR / "alembic.ini"), output_buffer=buf)
    command.downgrade(cfg, "0001:base", sql=True)
    out = buf.getvalue()
    assert "DROP TABLE IF EXISTS clinic" in out and "DROP TYPE IF EXISTS user_role" in out
