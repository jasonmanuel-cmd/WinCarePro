import json
import tempfile
import unittest
from pathlib import Path

from rollback_engine import RollbackEngine


class FakeRegistry:
    HKEY_CURRENT_USER = "HKCU"
    HKEY_LOCAL_MACHINE = "HKLM"
    KEY_SET_VALUE = 1
    KEY_QUERY_VALUE = 2
    REG_DWORD = 4
    REG_SZ = 1

    def __init__(self):
        self.values = {}
        self.fail_writes = False

    def OpenKey(self, hive, subkey, access=0, *args):
        return (hive, subkey)

    def CreateKeyEx(self, hive, subkey, reserved=0, access=0):
        return (hive, subkey)

    def CloseKey(self, key):
        return None

    def SetValueEx(self, key, name, reserved, value_type, value):
        if self.fail_writes:
            raise PermissionError("denied")
        if not isinstance(value_type, int):
            raise TypeError("registry type must be numeric")
        self.values[(key[0], key[1], name)] = (value, value_type)

    def QueryValueEx(self, key, name):
        lookup = (key[0], key[1], name)
        if lookup not in self.values:
            raise FileNotFoundError(name)
        return self.values[lookup]

    def DeleteValue(self, key, name):
        if self.fail_writes:
            raise PermissionError("denied")
        self.values.pop((key[0], key[1], name), None)


class RollbackEngineTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.ledger_path = Path(self.temp.name) / "ledger.json"
        self.registry = FakeRegistry()

    def tearDown(self):
        self.temp.cleanup()

    def engine(self):
        return RollbackEngine(backup_file=self.ledger_path, registry_backend=self.registry)

    def test_restores_typed_value_and_clears_only_verified_entry(self):
        engine = self.engine()
        engine.record_change(
            "privacy", "ExampleValue", r"HKCU\Software\Example", "previous", "changed",
            value_type=self.registry.REG_SZ, value_existed=True,
        )

        ok, message = engine.undo_all_changes()

        self.assertTrue(ok)
        self.assertIn("1", message)
        self.assertEqual(
            self.registry.values[("HKCU", r"Software\Example", "ExampleValue")],
            ("previous", self.registry.REG_SZ),
        )
        self.assertEqual(engine.ledger, [])

    def test_deletes_value_that_did_not_exist_before_change(self):
        engine = self.engine()
        self.registry.values[("HKCU", r"Software\Example", "NewValue")] = (1, self.registry.REG_DWORD)
        engine.record_change(
            "privacy", "NewValue", r"HKCU\Software\Example", None, 1,
            value_type=self.registry.REG_DWORD, value_existed=False,
        )

        ok, _ = engine.undo_all_changes()

        self.assertTrue(ok)
        self.assertNotIn(("HKCU", r"Software\Example", "NewValue"), self.registry.values)
        self.assertEqual(engine.ledger, [])

    def test_default_type_resolves_to_numeric_registry_dword(self):
        engine = self.engine()
        engine.record_change("privacy", "DefaultValue", r"HKCU\Software\Example", 0, 1)

        ok, _ = engine.undo_all_changes()

        self.assertTrue(ok)
        self.assertEqual(
            self.registry.values[("HKCU", r"Software\Example", "DefaultValue")],
            (0, self.registry.REG_DWORD),
        )

    def test_persist_failure_after_mutation_marks_ledger_in_progress_and_blocks_replay(self):
        engine = self.engine()
        engine.record_change(
            "privacy", "ExampleValue", r"HKCU\Software\Example", 0, 1,
            value_type=self.registry.REG_DWORD, value_existed=True,
        )
        original_save = engine._save_ledger
        calls = 0

        def fail_final_save():
            nonlocal calls
            calls += 1
            return original_save() if calls == 1 else False

        engine._save_ledger = fail_final_save
        ok, message = engine.undo_all_changes()

        self.assertFalse(ok)
        self.assertIn("could not be saved", message)
        on_disk = __import__("json").loads(self.ledger_path.read_text(encoding="utf-8"))
        self.assertEqual(on_disk[0]["rollback_status"], "in_progress")
        retry = RollbackEngine(backup_file=self.ledger_path, registry_backend=self.registry)
        retry_ok, retry_message = retry.undo_all_changes()
        self.assertFalse(retry_ok)
        self.assertIn("Recovery required", retry_message)

    def test_keeps_failed_entries_and_reports_failure(self):
        engine = self.engine()
        engine.record_change(
            "privacy", "ExampleValue", r"HKCU\Software\Example", 0, 1,
            value_type=self.registry.REG_DWORD, value_existed=True,
        )
        self.registry.fail_writes = True

        ok, message = engine.undo_all_changes()

        self.assertFalse(ok)
        self.assertIn("0 restored", message)
        self.assertEqual(len(engine.ledger), 1)
        self.assertTrue(self.ledger_path.exists())

    def test_rejects_legacy_entry_without_existence_metadata(self):
        engine = self.engine()
        engine.ledger = [{
            "category": "legacy", "item_name": "Old", "key_path": r"HKCU\Software\Example",
            "old_value": 0, "new_value": 1, "value_type": self.registry.REG_DWORD,
        }]

        ok, message = engine.undo_all_changes()

        self.assertFalse(ok)
        self.assertIn("metadata", message)
        self.assertEqual(len(engine.ledger), 1)


if __name__ == "__main__":
    unittest.main()
