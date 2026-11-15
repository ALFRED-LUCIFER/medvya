"""AES-256-GCM chunk encryption under a per-session DEK wrapped by the master key."""

import os
import uuid
from hashlib import sha256
from typing import Protocol

from cryptography.hazmat.primitives.ciphers.aead import AESGCM


class KeyWrapper(Protocol):
    """Key Vault unwrap is a follow-up behind this Protocol."""

    version: str

    def wrap(self, dek: bytes, session_id: uuid.UUID) -> bytes: ...
    def unwrap(self, wrapped: bytes, session_id: uuid.UUID) -> bytes: ...


class LocalKeyWrapper:
    version = "local-v1"

    def __init__(self, master_key: bytes) -> None:
        self._aead = AESGCM(master_key)

    @staticmethod
    def _aad(session_id: uuid.UUID) -> bytes:
        return b"dek|" + str(session_id).encode()

    def wrap(self, dek: bytes, session_id: uuid.UUID) -> bytes:
        nonce = os.urandom(12)
        return nonce + self._aead.encrypt(nonce, dek, self._aad(session_id))

    def unwrap(self, wrapped: bytes, session_id: uuid.UUID) -> bytes:
        return self._aead.decrypt(wrapped[:12], wrapped[12:], self._aad(session_id))


def new_dek() -> bytes:
    return AESGCM.generate_key(bit_length=256)


def chunk_aad(session_id: uuid.UUID, segment_no: int, seq: int) -> bytes:
    return f"{session_id}|{segment_no}|{seq}".encode()


def encrypt_chunk(dek: bytes, plaintext: bytes, aad: bytes) -> tuple[bytes, bytes]:
    """Returns (nonce, ciphertext+tag). Random 96-bit nonce per call."""
    nonce = os.urandom(12)
    return nonce, AESGCM(dek).encrypt(nonce, plaintext, aad)


def decrypt_chunk(dek: bytes, nonce: bytes, ciphertext: bytes, aad: bytes) -> bytes:
    return AESGCM(dek).decrypt(nonce, ciphertext, aad)


def sha256_hex(data: bytes) -> str:
    return sha256(data).hexdigest()
