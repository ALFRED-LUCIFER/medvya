import time
from typing import Any

import jwt
import pytest
from cryptography.hazmat.primitives.asymmetric import rsa

from medvya_ai.auth import JwtVerifier, jwks_url
from medvya_ai.config import Settings
from medvya_ai.errors import AppError

ISS, AUD = "https://login.microsoftonline.com/tid/v2.0", "api://medvya_ai"
KEY = rsa.generate_private_key(public_exponent=65537, key_size=2048)
PUB = KEY.public_key()


def token(**over: Any) -> str:
    now = int(time.time())
    claims = {"iss": ISS, "aud": AUD, "iat": now, "exp": now + 300, "oid": "user-1", "sub": "pw"}
    claims.update(over)
    return jwt.encode(claims, KEY, algorithm="RS256")


@pytest.fixture
def verifier() -> JwtVerifier:
    return JwtVerifier(issuer=ISS, audience=AUD, key_for=lambda _: PUB)


async def test_valid_token_yields_oid_subject(verifier: JwtVerifier) -> None:
    assert (await verifier.verify(token())).subject == "user-1"
    assert (await verifier.verify(token(oid=None))).subject == "pw"  # falls back to sub


@pytest.mark.parametrize(
    "bad",
    [
        {"exp": int(time.time()) - 3600},
        {"aud": "api://other"},
        {"iss": "https://evil.example/v2.0"},
        {"oid": None, "sub": None},
    ],
)
async def test_invalid_tokens_are_401(verifier: JwtVerifier, bad: dict[str, Any]) -> None:
    with pytest.raises(AppError) as exc:
        await verifier.verify(token(**bad))
    assert exc.value.status == 401 and exc.value.headers == {"WWW-Authenticate": "Bearer"}


async def test_wrong_key_and_none_alg_are_401(verifier: JwtVerifier) -> None:
    other = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    now = int(time.time())
    claims = {"iss": ISS, "aud": AUD, "iat": now, "exp": now + 60, "oid": "x"}
    for forged in (
        jwt.encode(claims, other, algorithm="RS256"),
        jwt.encode(claims, "", algorithm="none"),
    ):
        with pytest.raises(AppError):
            await verifier.verify(forged)


def test_jwks_url_derived_for_entra_only(settings: Settings) -> None:
    s = settings.model_copy(update={"auth_issuer": ISS})
    assert jwks_url(s) == "https://login.microsoftonline.com/tid/discovery/v2.0/keys"
    s = settings.model_copy(update={"auth_issuer": "https://idp.example", "auth_jwks_url": "x"})
    assert jwks_url(s) == "x"
    with pytest.raises(RuntimeError, match="AUTH_JWKS_URL"):
        jwks_url(settings.model_copy(update={"auth_issuer": "https://idp.example"}))
