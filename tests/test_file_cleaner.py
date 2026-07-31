import os
import tempfile
import threading
import unittest
from pathlib import Path
from unittest import mock

from file_cleaner import FileCleaner


class FileCleanerTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.cleaner = FileCleaner()
        self.now = 2_000_000_000.0

    def tearDown(self):
        self.temp.cleanup()

    def write(self, name, content=b"x", age_days=0):
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(content)
        timestamp = self.now - age_days * 86400
        os.utime(path, (timestamp, timestamp))
        return path

    def scan(self, **kwargs):
        return self.cleaner.scan_candidates(
            [str(self.root)], now=self.now, **kwargs)

    def test_classifies_only_old_supported_artifacts(self):
        old_installer = self.write("setup.exe", b"installer", age_days=181)
        self.write("recent.msi", b"recent", age_days=179)
        self.write("old.txt", b"notes", age_days=400)

        records = self.scan()

        self.assertEqual([record["path"] for record in records], [str(old_installer)])
        self.assertEqual(records[0]["category"], "Old installer/archive")
        self.assertEqual(records[0]["age_days"], 181)

    def test_finds_all_file_type_duplicates_and_combines_categories(self):
        first = self.write("old.zip", b"same bytes", age_days=200)
        second = self.write("copy.data", b"same bytes", age_days=1)

        records = self.scan()

        self.assertEqual({record["path"] for record in records}, {str(first), str(second)})
        self.assertEqual({record["duplicate_group"] for record in records}, {1})
        combined = next(record for record in records if record["path"] == str(first))
        self.assertEqual(combined["category"], "Old installer/archive + Duplicate")

    def test_cancelled_scan_returns_no_partial_results(self):
        self.write("old.exe", b"x", age_days=200)
        cancel = threading.Event()
        cancel.set()

        self.assertEqual(self.scan(cancel_event=cancel), [])

    def test_excludes_project_tree_and_symlink(self):
        excluded = self.root / "WinCarePro"
        excluded.mkdir()
        hidden = excluded / "old.exe"
        hidden.write_bytes(b"hidden")
        timestamp = self.now - 200 * 86400
        os.utime(hidden, (timestamp, timestamp))
        link = self.root / "linked.exe"
        try:
            link.symlink_to(hidden)
        except OSError:
            link = None

        records = self.scan(exclude_paths=[str(excluded)])

        self.assertEqual(records, [])
        if link is not None:
            self.assertTrue(link.exists())

    def test_delete_revalidates_metadata_and_reports_partial_failure(self):
        stable = self.write("stable.exe", b"stable", age_days=200)
        changed = self.write("changed.exe", b"before", age_days=200)
        records = self.scan()
        changed.write_bytes(b"after scan")

        result = self.cleaner.delete_candidates(records, records)

        self.assertEqual(result["deleted_count"], 1)
        self.assertEqual(result["failed_count"], 1)
        self.assertEqual(result["freed_bytes"], len(b"stable"))
        self.assertFalse(stable.exists())
        self.assertTrue(changed.exists())

    def test_delete_preserves_one_when_every_duplicate_is_selected(self):
        first = self.write("a.bin", b"duplicate")
        second = self.write("b.bin", b"duplicate")
        records = self.scan(old_days=None)

        result = self.cleaner.delete_candidates(records, records)

        self.assertEqual(result["deleted_count"], 1)
        self.assertEqual(result["failed_count"], 1)
        self.assertEqual(sum(Path(item["path"]).exists() for item in records), 1)

    def test_delete_preserves_copy_when_unselected_duplicate_changed(self):
        first = self.write("a.bin", b"duplicate")
        second = self.write("b.bin", b"duplicate")
        records = self.scan(old_days=None)
        second.write_bytes(b"not a duplicate anymore")

        first_record = next(item for item in records if item["path"] == str(first))
        result = self.cleaner.delete_candidates([first_record], records)

        self.assertEqual(result["deleted_count"], 0)
        self.assertTrue(first.exists())

    def test_delete_rejects_record_not_returned_by_scan(self):
        outside = self.write("old.exe", b"installer", age_days=200)
        fake = {
            "path": str(outside), "size_bytes": outside.stat().st_size,
            "mtime_ns": outside.stat().st_mtime_ns,
        }

        result = self.cleaner.delete_candidates([fake], [])

        self.assertEqual(result["deleted_count"], 0)
        self.assertEqual(result["failed_count"], 1)
        self.assertTrue(outside.exists())

    def test_missing_and_permission_errors_are_reported(self):
        path = self.write("old.iso", b"image", age_days=200)
        records = self.scan()
        path.unlink()

        missing = self.cleaner.delete_candidates(records, records)
        self.assertEqual((missing["deleted_count"], missing["failed_count"]), (0, 1))

        path = self.write("blocked.iso", b"image", age_days=200)
        records = self.scan()
        with mock.patch.object(Path, "unlink", side_effect=PermissionError("blocked")):
            blocked = self.cleaner.delete_candidates(records, records)
        self.assertEqual((blocked["deleted_count"], blocked["failed_count"]), (0, 1))
        self.assertTrue(path.exists())


if __name__ == "__main__":
    unittest.main()
