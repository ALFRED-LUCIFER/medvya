import os
import uuid

import pytest
from cryptography.exceptions import InvalidTag

from medvya_ai import crypto

SID = uuid.uuid4()


def test_roundtrip_and_ciphertext_differs() -> None:
    dek = crypto.new_dek()
    aad = crypto.chunk_aad(SID, 0, 3)
    nonce, ct = crypto.encrypt_chunk(dek, b"audio-bytes", aad)
    assert ct != b"audio-bytes" and b"audio-bytes" not in ct
    assert crypto.decrypt_chunk(dek, nonce, ct, aad) == b"audio-bytes"


@pytest.mark.parametrize("segment_no,seq", [(1, 3), (0, 4)])
def test_wrong_aad_fails(segment_no: int, seq: int) -> None:
    dek = crypto.new_dek()
    nonce, ct = crypto.encrypt_chunk(dek, b"x", crypto.chunk_aad(SID, 0, 3))
    with pytest.raises(InvalidTag):
        crypto.decrypt_chunk(dek, nonce, ct, crypto.chunk_aad(SID, segment_no, seq))


def test_tamper_fails() -> None:
    dek = crypto.new_dek()
    aad = crypto.chunk_aad(SID, 0, 0)
    nonce, ct = crypto.encrypt_chunk(dek, b"hello", aad)
    with pytest.raises(InvalidTag):
        crypto.decrypt_chunk(dek, nonce, bytes([ct[0] ^ 1]) + ct[1:], aad)


def test_nonces_unique() -> None:
    dek = crypto.new_dek()
    assert len({crypto.encrypt_chunk(dek, b"x", b"a")[0] for _ in range(50)}) == 50


def test_key_wrap_roundtrip_bound_to_session() -> None:
    w = crypto.LocalKeyWrapper(os.urandom(32))
    dek = crypto.new_dek()
    wrapped = w.wrap(dek, SID)
    assert w.unwrap(wrapped, SID) == dek
    with pytest.raises(InvalidTag):
        w.unwrap(wrapped, uuid.uuid4())
    with pytest.raises(InvalidTag):
        crypto.LocalKeyWrapper(os.urandom(32)).unwrap(wrapped, SID)


def test_sha256_hex() -> None:
    assert (
        crypto.sha256_hex(b"") == "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
    )
