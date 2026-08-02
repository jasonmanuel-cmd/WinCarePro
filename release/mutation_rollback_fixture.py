"""Isolated real-registry mutation and rollback verification for release evidence."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
import sys
import tempfile
import uuid
import winreg

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from rollback_engine import RollbackEngine


KEY_PATH = r"HKCU\Software\WinCarePro\Verification"
SUBKEY = r"Software\WinCarePro\Verification"


def verify_fixture() -> dict[str, object]:
    value_name = f"RollbackFixture_{uuid.uuid4().hex}"
    with tempfile.TemporaryDirectory(prefix="wincarepro-rollback-") as temp_dir:
        engine = RollbackEngine(Path(temp_dir) / "ledger.json", winreg)
        recorded = engine.record_change(
            "release_fixture", value_name, KEY_PATH, None, 1,
            value_type=winreg.REG_DWORD, value_existed=False,
        )
        if not recorded:
            raise RuntimeError("The rollback intent could not be persisted; no mutation was made.")

        key = winreg.CreateKeyEx(winreg.HKEY_CURRENT_USER, SUBKEY, 0, winreg.KEY_SET_VALUE)
        try:
            winreg.SetValueEx(key, value_name, 0, winreg.REG_DWORD, 1)
        finally:
            winreg.CloseKey(key)

        query = winreg.OpenKey(winreg.HKEY_CURRENT_USER, SUBKEY, 0, winreg.KEY_QUERY_VALUE)
        try:
            mutated_value, mutated_type = winreg.QueryValueEx(query, value_name)
        finally:
            winreg.CloseKey(query)
        if (mutated_value, mutated_type) != (1, winreg.REG_DWORD):
            raise RuntimeError("Fixture mutation read-back verification failed.")

        rollback_ok, rollback_message = engine.undo_all_changes()
        query = winreg.OpenKey(winreg.HKEY_CURRENT_USER, SUBKEY, 0, winreg.KEY_QUERY_VALUE)
        try:
            try:
                winreg.QueryValueEx(query, value_name)
                absent = False
            except FileNotFoundError:
                absent = True
        finally:
            winreg.CloseKey(query)

        passed = rollback_ok and absent and not engine.ledger
        return {
            "schema_version": 1,
            "fixture": "isolated_hkcu_registry_rollback",
            "passed": passed,
            "intent_persisted_before_mutation": recorded,
            "mutation_read_back": True,
            "rollback_verified": rollback_ok and absent,
            "ledger_cleared": not engine.ledger,
            "message": rollback_message,
        }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--execute", action="store_true", help="Run the isolated HKCU fixture.")
    args = parser.parse_args()
    if not args.execute:
        parser.error("--execute is required")
    result = verify_fixture()
    print(json.dumps(result, sort_keys=True))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
