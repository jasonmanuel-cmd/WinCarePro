import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


class MainImportTests(unittest.TestCase):
    def test_importing_main_does_not_create_app_data_directories(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            env = dict(os.environ)
            env["LOCALAPPDATA"] = temp_dir
            env["PYTHONDONTWRITEBYTECODE"] = "1"
            result = subprocess.run(
                [sys.executable, "-c", "import main; print('IMPORTED')"],
                cwd=Path(__file__).resolve().parents[1],
                env=env,
                capture_output=True,
                text=True,
                timeout=30,
            )

            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertIn("IMPORTED", result.stdout)
            self.assertFalse((Path(temp_dir) / "WinCarePro").exists())


if __name__ == "__main__":
    unittest.main()
