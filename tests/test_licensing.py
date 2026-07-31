import unittest
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


if __name__ == "__main__":
    unittest.main()
