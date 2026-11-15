"""HTTP hardening: request size limits."""

from starlette.types import ASGIApp, Message, Receive, Scope, Send

from medvya_ai.errors import AppError

MAX_BODY_BYTES = 1024 * 1024  # JSON endpoints; chunk uploads use Settings.max_chunk_bytes


class BodyLimitMiddleware:
    """413 for a declared Content-Length over the limit; streamed bodies are counted as read
    (AppError raised from receive is turned into the error envelope by the app's handlers)."""

    def __init__(self, app: ASGIApp, max_chunk_bytes: int, max_body_bytes: int = MAX_BODY_BYTES):
        self.app, self.chunk_limit, self.body_limit = app, max_chunk_bytes, max_body_bytes

    async def __call__(self, scope: Scope, receive: Receive, send: Send) -> None:
        if scope["type"] != "http" or scope["method"] in ("GET", "HEAD", "OPTIONS"):
            await self.app(scope, receive, send)
            return
        limit = self.chunk_limit if "/chunks/" in scope["path"] else self.body_limit
        declared = dict(scope["headers"]).get(b"content-length")
        if declared is not None and declared.isdigit() and int(declared) > limit:
            await _too_large(scope, receive, send)
            return
        seen = 0

        async def counted() -> Message:
            nonlocal seen
            msg = await receive()
            if msg["type"] == "http.request":
                seen += len(msg.get("body", b""))
                if seen > limit:
                    raise AppError(413, "PAYLOAD_TOO_LARGE", "Request body too large")
            return msg

        await self.app(scope, counted, send)


async def _too_large(scope: Scope, receive: Receive, send: Send) -> None:
    from fastapi.responses import JSONResponse

    resp = JSONResponse(
        {"error": {"code": "PAYLOAD_TOO_LARGE", "message": "Request body too large"}}, 413
    )
    await resp(scope, receive, send)
