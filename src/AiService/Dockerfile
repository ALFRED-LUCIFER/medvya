# One image for the API and the worker (same code, different command). Build context: src/AiService.
#   docker build -t medvya-ai-service src/AiService
#   API:    docker run --env-file .env -p 8000:8000 medvya-ai-service
#   Worker: docker run --env-file .env medvya-ai-service python -m medvya_ai.worker
# No ffmpeg: WebM is a documented fast-transcription input (src/AiService/docs/provider-notes.md 1.2).
FROM python:3.13-slim AS build
COPY --from=ghcr.io/astral-sh/uv:0.11.8 /uv /usr/local/bin/uv
ENV UV_COMPILE_BYTECODE=1 UV_LINK_MODE=copy UV_PYTHON_DOWNLOADS=never
WORKDIR /app
COPY pyproject.toml uv.lock ./
RUN uv sync --frozen --no-dev --no-install-project
COPY src ./src
COPY alembic.ini ./
COPY migrations ./migrations
RUN uv sync --frozen --no-dev

FROM python:3.13-slim
RUN useradd --system --uid 10001 --no-create-home app
WORKDIR /app
COPY --from=build --chown=app:app /app /app
ENV PATH="/app/.venv/bin:$PATH" PYTHONUNBUFFERED=1 PYTHONDONTWRITEBYTECODE=1
USER app
EXPOSE 8000
# No HEALTHCHECK here: the worker runs this same image without an HTTP port. Probes live in the
# deployment config: the API container probes GET /healthz (liveness) and /readyz (readiness).
# --proxy-headers so the audit log sees the caller IP behind a load balancer.
CMD ["uvicorn", "medvya_ai.main:create_app", "--factory", "--host", "0.0.0.0", "--port", "8000", "--proxy-headers"]
