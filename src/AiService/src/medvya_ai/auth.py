"""Bearer-token identity (Entra ID). AUTH_MODE=service: the token proves the caller is Medvya's
API (allow-listed client id plus an app role); only then are the tenant and user it asserts in
headers trusted. AUTH_MODE=entra: the token's subject selects an app_user. AUTH_MODE=disabled
(development only) has no token."""

import asyncio
from collections.abc import Callable
from dataclasses import dataclass
from typing import Any, Protocol

import jwt
from fastapi import Request

from medvya_ai.config import Settings
from medvya_ai.errors import AppError

ALGORITHMS = ("RS256",)
LEEWAY_SECONDS = 60


@dataclass(frozen=True)
class Claims:
    subject: str  # Entra `oid` (stable per directory user), else `sub`
    client_id: str | None = None  # `azp` (v2 tokens) or `appid` (v1): the calling application
    roles: tuple[str, ...] = ()  # app roles granted to the caller


class TokenVerifier(Protocol):
    async def verify(self, token: str) -> Claims: ...


def _unauthenticated(message: str) -> AppError:
    return AppError(401, "UNAUTHENTICATED", message, headers={"WWW-Authenticate": "Bearer"})


def bearer_token(request: Request) -> str:
    scheme, _, token = request.headers.get("authorization", "").partition(" ")
    if scheme.lower() != "bearer" or not token.strip():
        raise _unauthenticated("Bearer token required")
    return token.strip()


class JwtVerifier:
    """Signature via `key_for(token)` (JWKS in production, a fixed key in tests), then issuer,
    audience, expiry and not-before are enforced by PyJWT."""

    def __init__(self, *, issuer: str, audience: str, key_for: Callable[[str], Any]) -> None:
        self._issuer, self._audience, self._key_for = issuer, audience, key_for

    def _decode(self, token: str) -> dict[str, Any]:
        key = self._key_for(token)
        payload: dict[str, Any] = jwt.decode(
            token,
            key,
            algorithms=list(ALGORITHMS),
            audience=self._audience,
            issuer=self._issuer,
            leeway=LEEWAY_SECONDS,
            options={"require": ["exp", "iat", "iss", "aud"]},
        )
        return payload

    async def verify(self, token: str) -> Claims:
        try:
            payload = await asyncio.to_thread(self._decode, token)  # JWKS fetch may block
        except jwt.PyJWTError as exc:
            raise _unauthenticated("Invalid or expired token") from exc
        subject = payload.get("oid") or payload.get("sub")
        if not isinstance(subject, str) or not subject:
            raise _unauthenticated("Token has no subject")
        client = payload.get("azp") or payload.get("appid")
        roles = payload.get("roles")
        return Claims(
            subject=subject,
            client_id=client if isinstance(client, str) else None,
            roles=tuple(r for r in roles if isinstance(r, str)) if isinstance(roles, list) else (),
        )


def authorise_service(claims: Claims, settings: Settings) -> None:
    """AUTH_MODE=service: a valid token is not enough; it must be Medvya's API with the app role."""
    if claims.client_id not in settings.csv(settings.auth_allowed_client_ids):
        raise AppError(403, "CALLER_NOT_ALLOWED", "This application may not call the AI service")
    if settings.auth_required_role not in claims.roles:
        raise AppError(403, "CALLER_NOT_ALLOWED", "The caller lacks the required app role")


def jwks_url(settings: Settings) -> str:
    if settings.auth_jwks_url:
        return settings.auth_jwks_url
    issuer = (settings.auth_issuer or "").rstrip("/")
    if issuer.startswith("https://login.microsoftonline.com/") and issuer.endswith("/v2.0"):
        return issuer.removesuffix("/v2.0") + "/discovery/v2.0/keys"
    raise RuntimeError("AUTH_JWKS_URL is required unless AUTH_ISSUER is an Entra ID v2.0 issuer")


def build_verifier(settings: Settings) -> TokenVerifier | None:
    """None in disabled mode. Otherwise a misconfiguration fails at startup, not per request."""
    if settings.auth_mode == "disabled":
        return None
    if not settings.auth_issuer or not settings.auth_audience:
        raise RuntimeError(
            f"AUTH_ISSUER and AUTH_AUDIENCE are required when AUTH_MODE={settings.auth_mode}"
        )
    if settings.auth_mode == "service" and not (
        settings.csv(settings.auth_allowed_client_ids) and settings.auth_required_role
    ):
        raise RuntimeError(
            "AUTH_ALLOWED_CLIENT_IDS and AUTH_REQUIRED_ROLE are required when AUTH_MODE=service"
        )
    client = jwt.PyJWKClient(jwks_url(settings), cache_keys=True, lifespan=3600)
    return JwtVerifier(
        issuer=settings.auth_issuer,
        audience=settings.auth_audience,
        key_for=lambda token: client.get_signing_key_from_jwt(token).key,
    )
