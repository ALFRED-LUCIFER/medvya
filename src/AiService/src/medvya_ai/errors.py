"""Error type and handlers. Wire shape: {"error": {"code", "message", ...extra}}."""

import logging
from typing import Any

from fastapi import FastAPI, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse
from starlette.exceptions import HTTPException as StarletteHTTPException


class AppError(Exception):
    def __init__(
        self,
        status: int,
        code: str,
        message: str = "",
        headers: dict[str, str] | None = None,
        **extra: Any,
    ) -> None:
        super().__init__(code)
        self.status, self.code, self.message = status, code, message or code
        self.headers, self.extra = headers, extra


log = logging.getLogger(__name__)


def _body(code: str, message: str, **extra: Any) -> dict[str, Any]:
    return {"error": {"code": code, "message": message, **extra}}


def install_handlers(app: FastAPI) -> None:
    @app.exception_handler(AppError)
    async def _app_error(_: Request, exc: AppError) -> JSONResponse:
        return JSONResponse(
            _body(exc.code, exc.message, **exc.extra), status_code=exc.status, headers=exc.headers
        )

    @app.exception_handler(StarletteHTTPException)
    async def _http_error(_: Request, exc: StarletteHTTPException) -> JSONResponse:
        return JSONResponse(_body(f"HTTP_{exc.status_code}", str(exc.detail)), exc.status_code)

    @app.exception_handler(RequestValidationError)
    async def _validation(_: Request, exc: RequestValidationError) -> JSONResponse:
        # loc + msg only: pydantic's "input" echo could carry PHI.
        details = [{"loc": [str(p) for p in e["loc"]], "msg": e["msg"]} for e in exc.errors()]
        return JSONResponse(_body("VALIDATION_ERROR", "Invalid request", details=details), 422)

    @app.exception_handler(Exception)
    async def _unhandled(_: Request, exc: Exception) -> JSONResponse:
        # Class name only: a database error's text can carry row values (PHI).
        log.error("unhandled error %s", type(exc).__name__)
        return JSONResponse(_body("INTERNAL_ERROR", "Internal error"), 500)
