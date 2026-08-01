import hashlib
import json
import tempfile
import types
import unittest
from pathlib import Path
from unittest import mock

from commerce import validated_checkout_url
from updater import UpdateClient, version_tuple


class _Response:
    def __init__(self, payload, status=200):
        self.payload = payload
        self.status = status
        self.offset = 0

    def __enter__(self):
        return self

    def __exit__(self, *_):
        return False

    def read(self, size=-1):
        if size < 0:
            size = len(self.payload)
        chunk = self.payload[self.offset:self.offset + size]
        self.offset += len(chunk)
        return chunk


class UpdateTests(unittest.TestCase):
    def test_version_comparison_is_numeric(self):
        self.assertGreater(version_tuple("1.10.0"), version_tuple("1.9.9"))
        with self.assertRaises(ValueError):
            version_tuple("latest")

    def test_urls_must_be_public_https(self):
        client = UpdateClient("https://updates.example.com/manifest.json",
                              "WinCare Pro LLC")
        self.assertTrue(client.configured)
        for url in ("file:///tmp/update", "http://example.com/update",
                    "https://user@example.com/update"):
            with self.subTest(url=url), self.assertRaises(ValueError):
                UpdateClient(url, "Publisher")
        self.assertEqual(
            validated_checkout_url("https://store.example.com/wincare"),
            "https://store.example.com/wincare")

    @mock.patch("updater.urllib.request.urlopen")
    def test_manifest_is_validated(self, urlopen):
        manifest = {
            "version": "1.4.0",
            "url": "https://updates.example.com/WinCarePro.exe",
            "sha256": "a" * 64,
        }
        urlopen.return_value = _Response(json.dumps(manifest).encode())
        result = UpdateClient(
            "https://updates.example.com/manifest.json",
            "WinCare Pro LLC").check("1.3.0")
        self.assertTrue(result["available"])

    @mock.patch("updater.urllib.request.urlopen")
    @mock.patch("updater.UpdateClient._verify_authenticode")
    def test_download_requires_hash_and_trusted_signature(self, verify, urlopen):
        payload = b"signed installer bytes"
        urlopen.return_value = _Response(payload)
        verify.return_value = None
        manifest = {
            "url": "https://updates.example.com/WinCarePro.exe",
            "sha256": hashlib.sha256(payload).hexdigest(),
        }
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "update.exe"
            result = UpdateClient(
                "https://updates.example.com/manifest.json",
                "WinCare Pro LLC").download_and_verify(
                    manifest, str(path))
            self.assertEqual(result.read_bytes(), payload)


if __name__ == "__main__":
    unittest.main()
