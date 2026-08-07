import os
import unittest
from unittest import mock

from security_scanner import SecurityScanner


class TestSecurityScanner(unittest.TestCase):
    def test_impostor_process_masquerading_check(self):
        scanner = SecurityScanner()
        mock_proc_info = {
            "pid": 1234,
            "name": "svchost.exe",
            "exe": "C:\\Users\\Blunt\\AppData\\Local\\Temp\\svchost.exe",
            "username": "blunt"
        }

        with mock.patch("psutil.process_iter") as mock_iter:
            mock_proc = mock.Mock()
            mock_proc.info = mock_proc_info
            mock_iter.return_value = [mock_proc]

            findings = scanner.scan_processes()
            self.assertEqual(len(findings), 1)
            self.assertEqual(findings[0]["category"], "Process Masquerading")
            self.assertEqual(findings[0]["type"], "Critical")

    def test_double_extension_trojan_check(self):
        scanner = SecurityScanner()
        mock_proc_info = {
            "pid": 5678,
            "name": "invoice.pdf.exe",
            "exe": "C:\\Users\\Blunt\\Downloads\\invoice.pdf.exe",
            "username": "blunt"
        }

        with mock.patch("psutil.process_iter") as mock_iter:
            mock_proc = mock.Mock()
            mock_proc.info = mock_proc_info
            mock_iter.return_value = [mock_proc]

            findings = scanner.scan_processes()
            self.assertEqual(len(findings), 1)
            self.assertEqual(findings[0]["category"], "Double Extension Trojan")
            self.assertEqual(findings[0]["type"], "Critical")

    def test_unsigned_execution_from_temp_paths(self):
        scanner = SecurityScanner()
        mock_proc_info = {
            "pid": 9101,
            "name": "miner.exe",
            "exe": "C:\\Users\\Blunt\\AppData\\Local\\Temp\\miner.exe",
            "username": "blunt"
        }

        with (
            mock.patch("psutil.process_iter") as mock_iter,
            mock.patch.object(scanner, "verify_signature_offline") as mock_sig
        ):
            mock_proc = mock.Mock()
            mock_proc.info = mock_proc_info
            mock_iter.return_value = [mock_proc]
            mock_sig.return_value = {"status": "NotSigned", "signer": "N/A", "valid": False}

            with mock.patch.dict(os.environ, {"TEMP": "C:\\Users\\Blunt\\AppData\\Local\\Temp"}):
                findings = scanner.scan_processes()
                self.assertEqual(len(findings), 1)
                self.assertEqual(findings[0]["category"], "Unsigned Writable Execution")
                self.assertEqual(findings[0]["type"], "Warning")

    def test_warnings_do_not_zero_security_score(self):
        scanner = SecurityScanner()
        warnings = [{"type": "Warning"}, {"type": "Warning"}]
        with mock.patch.object(scanner, "scan_processes", return_value=warnings), mock.patch.object(
            scanner, "scan_startup_persistence", return_value=[]
        ):
            result = scanner.run_security_suite()

        self.assertEqual(result["score"], 90)


if __name__ == "__main__":
    unittest.main()
