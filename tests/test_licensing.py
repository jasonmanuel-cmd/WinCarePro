import unittest
import tempfile
from pathlib import Path
from unittest import mock

import licensing


class LicensingTests(unittest.TestCase):
    def test_key_shape_never_grants_offline_access(self):
        manager = licensing.LicenseManager()
        self.assertFalse(manager.verify_key_offline("WCP-PRO-ANYTHING"))
        self.assertFalse(manager.verify_key_offline("12345678-1234-1234-1234-123456789012"))

    def test_network_failure_does_not_activate(self):
        manager = licensing.LicenseManager()
        with mock.patch("urllib.request.urlopen", side_effect=OSError("offline")):
            ok, message = manager.activate_online("WCP-PRO-ANYTHING")
        self.assertFalse(ok)
        self.assertIn("could not be verified", message)

    def test_secure_save_fails_closed_when_data_protection_fails(self):
        manager = licensing.LicenseManager()
        with mock.patch("licensing._dpapi", side_effect=OSError("DPAPI failed")):
            self.assertFalse(manager._save_license("KEY", "buyer@example.com"))
        self.assertFalse(manager.is_pro())

    def test_secure_save_and_load_use_protected_payload(self):
        with tempfile.TemporaryDirectory() as directory, mock.patch(
            "licensing.APP_DIR", Path(directory)
        ), mock.patch("licensing.LICENSE_FILE", Path(directory) / "license.dat"), mock.patch(
            "licensing._dpapi", side_effect=lambda data, protect: b"protected" if protect else data
        ):
            manager = licensing.LicenseManager()
            self.assertTrue(manager._save_license("key", "Buyer@Example.com"))
            self.assertNotIn(b"buyer@example.com", licensing.LICENSE_FILE.read_bytes())

    def test_windows_data_protection_round_trip(self):
        payload = b"customer-license-record"
        encrypted = licensing._dpapi(payload, protect=True)
        self.assertNotEqual(encrypted, payload)
        self.assertEqual(licensing._dpapi(encrypted, protect=False), payload)


if __name__ == "__main__":
    unittest.main()
