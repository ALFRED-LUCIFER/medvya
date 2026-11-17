"""Blob storage behind a Protocol: Azure for real, in-memory for tests."""

import asyncio
import contextlib
from typing import Protocol

from azure.core.exceptions import ResourceExistsError, ResourceNotFoundError
from azure.storage.blob import BlobServiceClient


class BlobStore(Protocol):
    async def put(self, path: str, data: bytes) -> None: ...
    async def get(self, path: str) -> bytes: ...
    async def delete(self, path: str) -> None: ...
    async def ping(self) -> None: ...


class MemoryBlobStore:
    def __init__(self) -> None:
        self.blobs: dict[str, bytes] = {}

    async def put(self, path: str, data: bytes) -> None:
        self.blobs[path] = data

    async def get(self, path: str) -> bytes:
        return self.blobs[path]

    async def delete(self, path: str) -> None:
        self.blobs.pop(path, None)

    async def ping(self) -> None:
        return None


class AzureBlobStore:
    """Sync SDK offloaded with asyncio.to_thread (avoids the aiohttp dependency)."""

    def __init__(self, connection_string: str, container: str) -> None:
        self._svc = BlobServiceClient.from_connection_string(connection_string)
        self._container = self._svc.get_container_client(container)
        self._ensured = False

    def _ensure(self) -> None:
        if not self._ensured:
            with contextlib.suppress(ResourceExistsError):
                self._container.create_container()
            self._ensured = True

    def _put(self, path: str, data: bytes) -> None:
        self._ensure()
        self._container.upload_blob(path, data, overwrite=True)

    def _get(self, path: str) -> bytes:
        return self._container.download_blob(path).readall()

    def _delete(self, path: str) -> None:
        with contextlib.suppress(ResourceNotFoundError):
            self._container.delete_blob(path)

    async def put(self, path: str, data: bytes) -> None:
        await asyncio.to_thread(self._put, path, data)

    async def get(self, path: str) -> bytes:
        return await asyncio.to_thread(self._get, path)

    async def delete(self, path: str) -> None:
        await asyncio.to_thread(self._delete, path)

    def _ping(self) -> None:
        self._container.get_container_properties()  # read-only: readiness never creates anything

    async def ping(self) -> None:
        await asyncio.to_thread(self._ping)
