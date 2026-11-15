"""Initial schema: tables, enums, append-only triggers, FORCE RLS, grants, views, indexes.

Revision ID: 0001
Revises:

Design notes
- Runs as the table owner (DATABASE_URL_MIGRATE). The app connects as medvya_ai_app: not the
  owner, not superuser, not BYPASSRLS. A superuser or BYPASSRLS owner ignores RLS, so never point
  DATABASE_URL at such a role (this migration refuses if medvya_ai_app is one).
- Tenant policy uses app_clinic_id(): NULLIF(current_setting('app.clinic_id', true), '')::uuid.
  missing_ok=true gives NULL when the setting was never set, and NULLIF covers the empty string a
  pooled connection returns after a transaction-local set_config has ended. Either way the policy
  evaluates to NULL, so an unscoped session sees ZERO rows (and cannot write), instead of erroring.
- Composite FKs (clinic_id, x_id) make a cross-tenant reference impossible at the DB level.
  Referential-integrity checks bypass RLS by design, so these still work under FORCE RLS.
- One statement per op.execute (asyncpg does not run multi-statement strings).
"""

from collections.abc import Iterable

from alembic import op

revision = "0001"
down_revision = None
branch_labels = None
depends_on = None

APP_ROLE = "medvya_ai_app"

ENUMS: dict[str, tuple[str, ...]] = {
    "user_role": ("doctor", "admin"),
    "session_state": (
        "created",
        "ready",
        "recording",
        "processing",
        "review",
        "approved",
        "failed",
        "closed_no_consent",
    ),
    "participant_role": ("doctor", "patient", "companion", "interpreter"),
    "consent_status": ("granted", "refused", "withdrawn"),
    "consent_method": ("verbal_attested", "digital_signature"),
    "consent_on_behalf_of": ("self", "guardian", "representative"),
    "segment_state": ("open", "complete"),
    "note_template": ("soap", "concise"),
    "note_status": ("draft", "approved"),
    "note_author_type": ("ai", "clinician"),
    "fact_category": (
        "symptom",
        "diagnosis",
        "medication",
        "allergy",
        "vital",
        "exam_finding",
        "investigation",
        "follow_up",
        "advice",
        "history",
    ),
    "fact_polarity": ("present", "absent", "uncertain"),
    "fact_review_state": ("ai", "confirmed", "rejected"),
    "frequency_code": ("OD", "BD", "TDS", "QID", "HS", "SOS", "STAT", "PRN", "OTHER"),
    "duration_unit": ("day", "week", "month"),
    "medication_route": ("oral", "topical", "iv", "im", "inhalation", "other"),
    "job_kind": ("transcribe", "normalise", "extract", "retention_sweep"),
    "job_status": ("queued", "running", "done", "failed"),
}

# Tables with RLS (everything with PHI). clinic and processing_job intentionally have none.
RLS_TABLES = (
    "app_user",
    "retention_policy",
    "consultation_session",
    "participant",
    "consent_record",
    "recording_segment",
    "audio_chunk",
    "transcript_segment",
    "clinical_note",
    "note_revision",
    "clinical_fact",
    "medication",
    "fact_evidence",
    "extraction_rejection",
    "audit_event",
)
APPEND_ONLY = ("consent_record", "note_revision", "audit_event")

TABLES = (
    """CREATE TABLE clinic (
        id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
        name text NOT NULL,
        created_at timestamptz NOT NULL DEFAULT now()
    )""",
    """CREATE TABLE app_user (
        id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
        clinic_id uuid NOT NULL REFERENCES clinic(id),
        email text NOT NULL,
        password_hash text NOT NULL,
        role user_role NOT NULL,
        display_name text NOT NULL,
        is_active boolean NOT NULL DEFAULT true,
        created_at timestamptz NOT NULL DEFAULT now(),
        CONSTRAINT uq_app_user_email UNIQUE (email),
        CONSTRAINT ck_app_user_email_lower CHECK (email = lower(email)),
        CONSTRAINT uq_app_user_clinic_id UNIQUE (clinic_id, id)
    )""",
    """CREATE TABLE retention_policy (
        clinic_id uuid PRIMARY KEY REFERENCES clinic(id),
        audio_after_approval_hours integer NOT NULL DEFAULT 24
            CONSTRAINT ck_ret_hours CHECK (audio_after_approval_hours > 0),
        draft_audio_days integer NOT NULL DEFAULT 7
            CONSTRAINT ck_ret_days CHECK (draft_audio_days > 0),
        record_retention_years integer
    )""",
    """CREATE TABLE consultation_session (
        id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
        clinic_id uuid NOT NULL REFERENCES clinic(id),
        doctor_id uuid NOT NULL,
        patient_ref text NOT NULL,
        specialty text,
        scheduled_date date NOT NULL,
        state session_state NOT NULL DEFAULT 'created',
        recording_allowed boolean NOT NULL DEFAULT false,
        dek_wrapped bytea NOT NULL,
        kek_version text NOT NULL,
        finished_at timestamptz,
        approved_at timestamptz,
        created_at timestamptz NOT NULL DEFAULT now(),
        CONSTRAINT uq_session_clinic_id UNIQUE (clinic_id, id),
        CONSTRAINT fk_session_doctor FOREIGN KEY (clinic_id, doctor_id)
            REFERENCES app_user (clinic_id, id)
    )""",
    """CREATE TABLE participant (
        id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
        clinic_id uuid NOT NULL REFERENCES clinic(id),
        session_id uuid NOT NULL,
        role participant_role NOT NULL,
        display_name text NOT NULL,
        speaker_label text,
        created_at timestamptz NOT NULL DEFAULT now(),
        CONSTRAINT uq_participant_clinic_id UNIQUE (clinic_id, id),
        CONSTRAINT fk_participant_session FOREIGN KEY (clinic_id, session_id)
            REFERENCES consultation_session (clinic_id, id)
    )""",
    """CREATE TABLE consent_record (
        id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
        clinic_id uuid NOT NULL REFERENCES clinic(id),
        session_id uuid NOT NULL,
        participant_id uuid NOT NULL,
        status consent_status NOT NULL,
        method consent_method NOT NULL,
        on_behalf_of consent_on_behalf_of NOT NULL,
        attested_by_user_id uuid NOT NULL,
        notice_version text NOT NULL,
        notice_locale text NOT NULL,
        created_at timestamptz NOT NULL DEFAULT now(),
        CONSTRAINT fk_consent_session FOREIGN KEY (clinic_id, session_id)
            REFERENCES consultation_session (clinic_id, id),
        CONSTRAINT fk_consent_participant FOREIGN KEY (clinic_id, participant_id)
            REFERENCES participant (clinic_id, id),
        CONSTRAINT fk_consent_attester FOREIGN KEY (clinic_id, attested_by_user_id)
            REFERENCES app_user (clinic_id, id)
    )""",
    """CREATE TABLE recording_segment (
        id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
        clinic_id uuid NOT NULL REFERENCES clinic(id),
        session_id uuid NOT NULL,
        segment_no integer NOT NULL CONSTRAINT ck_segment_no CHECK (segment_no >= 0),
        start_offset_ms bigint NOT NULL CONSTRAINT ck_segment_offset CHECK (start_offset_ms >= 0),
        duration_ms bigint,
        chunk_count integer,
        mime_type text NOT NULL,
        state segment_state NOT NULL DEFAULT 'open',
        client_events jsonb NOT NULL DEFAULT '[]'::jsonb,
        created_at timestamptz NOT NULL DEFAULT now(),
        completed_at timestamptz,
        CONSTRAINT uq_segment_session_no UNIQUE (session_id, segment_no),
        CONSTRAINT uq_segment_clinic_id UNIQUE (clinic_id, id),
        CONSTRAINT fk_segment_session FOREIGN KEY (clinic_id, session_id)
            REFERENCES consultation_session (clinic_id, id)
    )""",
    """CREATE TABLE audio_chunk (
        id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
        clinic_id uuid NOT NULL REFERENCES clinic(id),
        session_id uuid NOT NULL,
        segment_id uuid NOT NULL,
        seq integer NOT NULL CONSTRAINT ck_chunk_seq CHECK (seq >= 0),
        size_bytes integer NOT NULL CONSTRAINT ck_chunk_size CHECK (size_bytes >= 0),
        sha256 char(64) NOT NULL,
        blob_path text NOT NULL,
        nonce bytea NOT NULL CONSTRAINT ck_chunk_nonce CHECK (octet_length(nonce) = 12),
        deleted_at timestamptz,
        created_at timestamptz NOT NULL DEFAULT now(),
        CONSTRAINT uq_chunk_segment_seq UNIQUE (segment_id, seq),
        CONSTRAINT fk_chunk_session FOREIGN KEY (clinic_id, session_id)
            REFERENCES consultation_session (clinic_id, id),
        CONSTRAINT fk_chunk_segment FOREIGN KEY (clinic_id, segment_id)
            REFERENCES recording_segment (clinic_id, id)
    )""",
    """CREATE TABLE transcript_segment (
        id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
        clinic_id uuid NOT NULL REFERENCES clinic(id),
        session_id uuid NOT NULL,
        recording_segment_id uuid NOT NULL,
        idx integer NOT NULL,
        speaker_label text NOT NULL,
        original_text text NOT NULL,
        language text NOT NULL,
        english_text text,
        start_ms bigint NOT NULL,
        end_ms bigint NOT NULL,
        confidence numeric(5,4),
        edited boolean NOT NULL DEFAULT false,
        CONSTRAINT uq_transcript_session_idx UNIQUE (session_id, idx),
        CONSTRAINT uq_transcript_clinic_id UNIQUE (clinic_id, id),
        CONSTRAINT fk_transcript_session FOREIGN KEY (clinic_id, session_id)
            REFERENCES consultation_session (clinic_id, id),
        CONSTRAINT fk_transcript_recording FOREIGN KEY (clinic_id, recording_segment_id)
            REFERENCES recording_segment (clinic_id, id)
    )""",
    # current_revision_id FK is added after note_revision exists (circular reference).
    """CREATE TABLE clinical_note (
        id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
        clinic_id uuid NOT NULL REFERENCES clinic(id),
        session_id uuid NOT NULL,
        template note_template NOT NULL DEFAULT 'soap',
        status note_status NOT NULL DEFAULT 'draft',
        current_revision_id uuid,
        approved_by uuid,
        approved_at timestamptz,
        created_at timestamptz NOT NULL DEFAULT now(),
        CONSTRAINT uq_note_session UNIQUE (session_id),
        CONSTRAINT uq_note_clinic_id UNIQUE (clinic_id, id),
        CONSTRAINT fk_note_session FOREIGN KEY (clinic_id, session_id)
            REFERENCES consultation_session (clinic_id, id),
        CONSTRAINT fk_note_approver FOREIGN KEY (clinic_id, approved_by)
            REFERENCES app_user (clinic_id, id)
    )""",
    """CREATE TABLE note_revision (
        id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
        clinic_id uuid NOT NULL REFERENCES clinic(id),
        note_id uuid NOT NULL,
        rev_no integer NOT NULL CONSTRAINT ck_rev_no CHECK (rev_no >= 1),
        author_type note_author_type NOT NULL,
        author_id uuid,
        content jsonb NOT NULL,
        created_at timestamptz NOT NULL DEFAULT now(),
        CONSTRAINT uq_revision_note_no UNIQUE (note_id, rev_no),
        CONSTRAINT uq_revision_clinic_id UNIQUE (clinic_id, id),
        CONSTRAINT fk_revision_note FOREIGN KEY (clinic_id, note_id)
            REFERENCES clinical_note (clinic_id, id),
        CONSTRAINT fk_revision_author FOREIGN KEY (clinic_id, author_id)
            REFERENCES app_user (clinic_id, id)
    )""",
    """ALTER TABLE clinical_note ADD CONSTRAINT fk_note_current_revision
        FOREIGN KEY (clinic_id, current_revision_id) REFERENCES note_revision (clinic_id, id)
        DEFERRABLE INITIALLY DEFERRED""",
    """CREATE TABLE clinical_fact (
        id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
        clinic_id uuid NOT NULL REFERENCES clinic(id),
        session_id uuid NOT NULL,
        note_id uuid NOT NULL,
        category fact_category NOT NULL,
        label text NOT NULL,
        polarity fact_polarity NOT NULL DEFAULT 'present',
        body_site text,
        value_numeric numeric,
        value_unit text,
        detail text,
        code_system text,
        code text,
        confidence numeric(5,4),
        review_state fact_review_state NOT NULL DEFAULT 'ai',
        created_at timestamptz NOT NULL DEFAULT now(),
        CONSTRAINT uq_fact_clinic_id UNIQUE (clinic_id, id),
        CONSTRAINT uq_fact_id_category UNIQUE (id, category),
        CONSTRAINT fk_fact_session FOREIGN KEY (clinic_id, session_id)
            REFERENCES consultation_session (clinic_id, id),
        CONSTRAINT fk_fact_note FOREIGN KEY (clinic_id, note_id)
            REFERENCES clinical_note (clinic_id, id)
    )""",
    # medication.category + FK (fact_id, category) guarantees the parent fact is a medication.
    # clinic_id is added to the plan's column list so RLS and analytics need no join.
    """CREATE TABLE medication (
        fact_id uuid PRIMARY KEY,
        clinic_id uuid NOT NULL REFERENCES clinic(id),
        category fact_category NOT NULL DEFAULT 'medication'
            CONSTRAINT ck_medication_category CHECK (category = 'medication'),
        name_as_spoken text NOT NULL,
        generic_name text,
        strength_value numeric,
        strength_unit text,
        dose_text text,
        frequency_code frequency_code,
        frequency_text text,
        duration_value integer,
        duration_unit duration_unit,
        route medication_route,
        instructions text,
        CONSTRAINT ck_medication_generic_lower CHECK (generic_name IS NULL OR generic_name = lower(generic_name)),
        CONSTRAINT fk_medication_fact FOREIGN KEY (fact_id, category)
            REFERENCES clinical_fact (id, category),
        CONSTRAINT fk_medication_clinic_fact FOREIGN KEY (clinic_id, fact_id)
            REFERENCES clinical_fact (clinic_id, id)
    )""",
    """CREATE TABLE fact_evidence (
        id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
        clinic_id uuid NOT NULL REFERENCES clinic(id),
        fact_id uuid NOT NULL,
        segment_id uuid NOT NULL,
        quote text NOT NULL CONSTRAINT ck_evidence_quote CHECK (length(btrim(quote)) > 0),
        start_char integer,
        CONSTRAINT fk_evidence_fact FOREIGN KEY (clinic_id, fact_id)
            REFERENCES clinical_fact (clinic_id, id),
        CONSTRAINT fk_evidence_segment FOREIGN KEY (clinic_id, segment_id)
            REFERENCES transcript_segment (clinic_id, id)
    )""",
    """CREATE TABLE extraction_rejection (
        id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
        clinic_id uuid NOT NULL REFERENCES clinic(id),
        session_id uuid NOT NULL,
        payload jsonb NOT NULL,
        reason text NOT NULL
            CONSTRAINT ck_rejection_reason CHECK (reason IN ('QUOTE_NOT_FOUND', 'SEGMENT_UNKNOWN', 'SCHEMA')),
        created_at timestamptz NOT NULL DEFAULT now(),
        CONSTRAINT fk_rejection_session FOREIGN KEY (clinic_id, session_id)
            REFERENCES consultation_session (clinic_id, id)
    )""",
    # No PHI, no RLS: the worker claims jobs across tenants, then scopes by the job's clinic_id.
    # session_id is NULL for per-clinic retention sweeps.
    """CREATE TABLE processing_job (
        id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
        clinic_id uuid NOT NULL REFERENCES clinic(id),
        session_id uuid,
        kind job_kind NOT NULL,
        status job_status NOT NULL DEFAULT 'queued',
        attempts integer NOT NULL DEFAULT 0,
        max_attempts integer NOT NULL DEFAULT 5,
        run_after timestamptz NOT NULL DEFAULT now(),
        locked_at timestamptz,
        locked_by text,
        last_error text,
        created_at timestamptz NOT NULL DEFAULT now(),
        updated_at timestamptz NOT NULL DEFAULT now(),
        CONSTRAINT fk_job_session FOREIGN KEY (clinic_id, session_id)
            REFERENCES consultation_session (clinic_id, id)
    )""",
    """CREATE TABLE audit_event (
        id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
        clinic_id uuid NOT NULL REFERENCES clinic(id),
        actor_user_id uuid,
        action text NOT NULL,
        entity_type text NOT NULL,
        entity_id uuid,
        metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
        ip text,
        created_at timestamptz NOT NULL DEFAULT now(),
        CONSTRAINT fk_audit_actor FOREIGN KEY (clinic_id, actor_user_id)
            REFERENCES app_user (clinic_id, id)
    )""",
)

INDEXES = (
    # clinic_id-leading composites on every tenant table
    "CREATE INDEX ix_app_user_clinic ON app_user (clinic_id)",
    "CREATE INDEX ix_session_clinic_created ON consultation_session (clinic_id, created_at DESC)",
    "CREATE INDEX ix_session_clinic_approved ON consultation_session (clinic_id, approved_at)",
    "CREATE INDEX ix_session_clinic_state ON consultation_session (clinic_id, state)",
    "CREATE INDEX ix_participant_clinic_session ON participant (clinic_id, session_id)",
    "CREATE INDEX ix_consent_clinic_participant ON consent_record "
    "(clinic_id, participant_id, created_at DESC, id DESC)",
    "CREATE INDEX ix_consent_clinic_session ON consent_record (clinic_id, session_id)",
    "CREATE INDEX ix_segment_clinic_session ON recording_segment (clinic_id, session_id)",
    "CREATE INDEX ix_chunk_clinic_session ON audio_chunk (clinic_id, session_id)",
    "CREATE INDEX ix_chunk_live ON audio_chunk (clinic_id, session_id) WHERE deleted_at IS NULL",
    "CREATE INDEX ix_transcript_clinic_session ON transcript_segment (clinic_id, session_id, idx)",
    "CREATE INDEX ix_transcript_fts ON transcript_segment "
    "USING gin (to_tsvector('english', coalesce(english_text, '')))",
    "CREATE INDEX ix_note_clinic_status ON clinical_note (clinic_id, status)",
    "CREATE INDEX ix_revision_clinic_note ON note_revision (clinic_id, note_id)",
    # analytics (plan: Analytics indexes)
    "CREATE INDEX ix_fact_clinic_category_created ON clinical_fact (clinic_id, category, created_at)",
    "CREATE INDEX ix_fact_clinic_label ON clinical_fact (clinic_id, label)",
    "CREATE INDEX ix_fact_clinic_session ON clinical_fact (clinic_id, session_id)",
    "CREATE INDEX ix_medication_generic ON medication (generic_name)",
    "CREATE INDEX ix_medication_clinic_generic ON medication (clinic_id, generic_name)",
    "CREATE INDEX ix_evidence_fact ON fact_evidence (fact_id)",
    "CREATE INDEX ix_evidence_clinic_fact ON fact_evidence (clinic_id, fact_id)",
    "CREATE INDEX ix_rejection_clinic_session ON extraction_rejection (clinic_id, session_id)",
    # queue
    "CREATE INDEX ix_job_status_run_after ON processing_job (status, run_after)",
    "CREATE UNIQUE INDEX uq_job_active_session_kind ON processing_job (session_id, kind) "
    "WHERE status IN ('queued', 'running')",
    "CREATE UNIQUE INDEX uq_job_active_clinic_sweep ON processing_job (clinic_id, kind) "
    "WHERE session_id IS NULL AND status IN ('queued', 'running')",
    # audit
    "CREATE INDEX ix_audit_clinic_created ON audit_event (clinic_id, created_at DESC)",
    "CREATE INDEX ix_audit_clinic_entity ON audit_event (clinic_id, entity_type, entity_id)",
)

FUNCTIONS = (
    # Tenant scoping. STABLE, so the planner can use clinic_id indexes.
    """CREATE FUNCTION app_clinic_id() RETURNS uuid
        LANGUAGE sql STABLE
        AS $$ SELECT NULLIF(current_setting('app.clinic_id', true), '')::uuid $$""",
    """CREATE FUNCTION forbid_mutation() RETURNS trigger
        LANGUAGE plpgsql
        AS $$
        BEGIN
            RAISE EXCEPTION USING
                MESSAGE = TG_TABLE_NAME || ' is append-only',
                ERRCODE = 'restrict_violation';
        END
        $$""",
)

# Both views are created WITH (security_invoker = true) so the caller's RLS applies.
VIEWS = (
    """CREATE VIEW v_current_consent WITH (security_invoker = true) AS
        SELECT DISTINCT ON (participant_id)
            id, clinic_id, session_id, participant_id, status, method, on_behalf_of,
            attested_by_user_id, notice_version, notice_locale, created_at
        FROM consent_record
        ORDER BY participant_id, created_at DESC, id DESC""",
    # Day buckets are UTC calendar days of approval. Drafts are excluded by construction.
    """CREATE VIEW v_medication_usage WITH (security_invoker = true) AS
        SELECT m.clinic_id,
               (s.approved_at AT TIME ZONE 'UTC')::date AS day,
               m.generic_name,
               count(*) AS prescriptions,
               count(DISTINCT s.id) AS sessions
        FROM medication m
        JOIN clinical_fact f ON f.id = m.fact_id AND f.clinic_id = m.clinic_id
        JOIN consultation_session s ON s.id = f.session_id AND s.clinic_id = f.clinic_id
        WHERE s.state = 'approved'
          AND s.approved_at IS NOT NULL
          AND f.review_state <> 'rejected'
          AND m.generic_name IS NOT NULL
        GROUP BY m.clinic_id, (s.approved_at AT TIME ZONE 'UTC')::date, m.generic_name""",
    """CREATE VIEW v_fact_counts WITH (security_invoker = true) AS
        SELECT f.clinic_id,
               (s.approved_at AT TIME ZONE 'UTC')::date AS day,
               f.category,
               f.label,
               count(*) AS n
        FROM clinical_fact f
        JOIN consultation_session s ON s.id = f.session_id AND s.clinic_id = f.clinic_id
        WHERE s.state = 'approved'
          AND s.approved_at IS NOT NULL
          AND f.review_state <> 'rejected'
        GROUP BY f.clinic_id, (s.approved_at AT TIME ZONE 'UTC')::date, f.category, f.label""",
)

ROLE_BOOTSTRAP = f"""DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{APP_ROLE}') THEN
        CREATE ROLE {APP_ROLE} LOGIN NOSUPERUSER NOBYPASSRLS;
    END IF;
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{APP_ROLE}' AND (rolsuper OR rolbypassrls)) THEN
        RAISE EXCEPTION '{APP_ROLE} must not be SUPERUSER or BYPASSRLS (RLS would be ignored)';
    END IF;
END
$$"""

MUTABLE = (
    "app_user",
    "retention_policy",
    "consultation_session",
    "participant",
    "recording_segment",
    "audio_chunk",
    "transcript_segment",
    "clinical_note",
    "clinical_fact",
    "medication",
    "fact_evidence",
    "extraction_rejection",
)


def _run(statements: Iterable[str]) -> None:
    for stmt in statements:
        op.execute(stmt)


def upgrade() -> None:
    op.execute(ROLE_BOOTSTRAP)
    _run(
        f"CREATE TYPE {name} AS ENUM ({', '.join(repr(v) for v in values)})"
        for name, values in ENUMS.items()
    )
    _run(FUNCTIONS)
    _run(TABLES)
    _run(INDEXES)

    # Append-only: row-level UPDATE/DELETE and statement-level TRUNCATE all raise.
    for table in APPEND_ONLY:
        op.execute(
            f"CREATE TRIGGER trg_{table}_no_mutation BEFORE UPDATE OR DELETE ON {table} "
            "FOR EACH ROW EXECUTE FUNCTION forbid_mutation()"
        )
        op.execute(
            f"CREATE TRIGGER trg_{table}_no_truncate BEFORE TRUNCATE ON {table} "
            "FOR EACH STATEMENT EXECUTE FUNCTION forbid_mutation()"
        )

    # Row level security: ENABLE + FORCE (the owner is subject too, unless superuser/BYPASSRLS).
    for table in RLS_TABLES:
        op.execute(f"ALTER TABLE {table} ENABLE ROW LEVEL SECURITY")
        op.execute(f"ALTER TABLE {table} FORCE ROW LEVEL SECURITY")
        op.execute(
            f"CREATE POLICY tenant_isolation ON {table} "
            "USING (clinic_id = app_clinic_id()) WITH CHECK (clinic_id = app_clinic_id())"
        )
    # Login must find a user by email before the clinic is known. The app sets the transaction-local
    # app.login_email for that one SELECT; email is globally unique so at most one row matches.
    op.execute(
        "CREATE POLICY login_lookup ON app_user FOR SELECT "
        "USING (email = NULLIF(current_setting('app.login_email', true), ''))"
    )

    _run(VIEWS)

    # Grants: least privilege, no DELETE anywhere (deletion workflows are a follow-up).
    op.execute(f"GRANT USAGE ON SCHEMA public TO {APP_ROLE}")
    op.execute(f"GRANT SELECT, INSERT ON clinic TO {APP_ROLE}")
    op.execute(f"GRANT SELECT, INSERT, UPDATE ON {', '.join(MUTABLE)} TO {APP_ROLE}")
    op.execute(f"GRANT SELECT, INSERT, UPDATE ON processing_job TO {APP_ROLE}")
    op.execute(f"GRANT SELECT, INSERT ON {', '.join(APPEND_ONLY)} TO {APP_ROLE}")
    op.execute(f"GRANT USAGE ON SEQUENCE audit_event_id_seq TO {APP_ROLE}")
    op.execute(
        f"GRANT SELECT ON v_current_consent, v_medication_usage, v_fact_counts TO {APP_ROLE}"
    )
    op.execute(f"REVOKE UPDATE, DELETE, TRUNCATE ON audit_event FROM {APP_ROLE}")
    op.execute("REVOKE UPDATE, DELETE, TRUNCATE ON audit_event FROM PUBLIC")


def downgrade() -> None:
    for view in ("v_fact_counts", "v_medication_usage", "v_current_consent"):
        op.execute(f"DROP VIEW IF EXISTS {view}")
    op.execute(
        "ALTER TABLE IF EXISTS clinical_note DROP CONSTRAINT IF EXISTS fk_note_current_revision"
    )
    for table in (
        "audit_event",
        "processing_job",
        "extraction_rejection",
        "fact_evidence",
        "medication",
        "clinical_fact",
        "note_revision",
        "clinical_note",
        "transcript_segment",
        "audio_chunk",
        "recording_segment",
        "consent_record",
        "participant",
        "consultation_session",
        "retention_policy",
        "app_user",
        "clinic",
    ):
        op.execute(f"DROP TABLE IF EXISTS {table}")  # triggers, policies, indexes go with it
    op.execute("DROP FUNCTION IF EXISTS forbid_mutation()")
    op.execute("DROP FUNCTION IF EXISTS app_clinic_id()")
    for name in reversed(list(ENUMS)):
        op.execute(f"DROP TYPE IF EXISTS {name}")
    # The medvya_ai_app role is cluster-wide and shared with other databases: left in place.
