"""
Regression tests for PowerShell command injection fixes (CWE-78).
All migrated call sites must use $args[N] positional pattern, not string interpolation.
"""
import os
import subprocess
import sys
from pathlib import Path
from unittest import mock

import pytest

REPO_ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(REPO_ROOT))


def _call_args_list_scripts(run_mock):
    """Extract the PowerShell script string from each subprocess.run call."""
    scripts = []
    for call in run_mock.call_args_list:
        args, _ = call
        cmd = args[0]
        if isinstance(cmd, list) and len(cmd) >= 5 and cmd[0] == "powershell" and cmd[3] == "-Command":
            scripts.append(cmd[4])
    return scripts


class TestRestorePointInjectionFix:
    """main.py RepairEngine.create_restore_point uses safe_ps with $args[0]."""

    def test_restore_point_label_passed_as_positional_arg(self):
        with mock.patch("main.subprocess.run"):
            with mock.patch("core.platform.IS_WINDOWS", True):
                with mock.patch("core.repair.safe_ps") as safe_ps:
                    import main
                    
                    safe_ps.return_value = (0, "Success")

                    label = "test'); Write-Output 'PWNED'; #"
                    class MockLogger:
                        def log(self, *args, **kwargs): pass
                    engine = main.RepairEngine(MockLogger())
                    engine.create_restore_point(lambda x: None, label)

                    safe_ps.assert_called_once()
                    args, _ = safe_ps.call_args
                    script = args[0]
                    label_arg = args[1]

                    assert "$args[0]" in script, f"Script must use $args[0], got: {script}"
                    # Label is sanitized by the method before passing to safe_ps
                    expected_label = label.replace("'", "").replace('"', "")[:60]
                    assert label_arg == expected_label
                    assert label not in script


class TestInspectProcessSignatureInjectionFix:
    """main.py inspect_process_signature uses safe_ps with $args[0]."""

    def test_inspect_process_signature_path_passed_as_positional_arg(self):
        with mock.patch("main.subprocess.run"):
            with mock.patch("main.IS_WINDOWS", True):
                with mock.patch("main.safe_ps") as safe_ps:
                    import main
                    
                    safe_ps.return_value = (0, "Valid")

                    path = r"C:\Windows\System32\test.exe'); Write-Output 'PWNED'; #"
                    main.inspect_process_signature(path)

                    safe_ps.assert_called_once()
                    args, _ = safe_ps.call_args
                    script = args[0]
                    path_arg = args[1]

                    assert "$args[0]" in script, f"Script must use $args[0], got: {script}"
                    assert path_arg == path
                    assert path not in script


class TestResolveServiceNameInjectionFix:
    """main.py EventTriage.resolve_service_name uses safe_ps with $args[0]."""

    def test_resolve_service_name_display_passed_as_positional_arg(self):
        with mock.patch("main.subprocess.run"):
            with mock.patch("core.platform.IS_WINDOWS", True):
                with mock.patch("core.events.safe_ps") as safe_ps:
                    import main
                    
                    safe_ps.return_value = (0, "wuauserv")

                    display = "Windows Update'); Write-Output 'PWNED'; #"
                    main.EventTriage.resolve_service_name(display)

                    safe_ps.assert_called_once()
                    args, _ = safe_ps.call_args
                    script = args[0]
                    display_arg = args[1]

                    assert "$args[0]" in script, f"Script must use $args[0], got: {script}"
                    assert display_arg == display
                    assert display not in script


class TestBloatRemoverUWPInjectionFix:
    """bloat_remover.py uninstall_uwp_app uses run_powershell_cmd with $args[0]."""

    def test_uninstall_uwp_package_passed_as_positional_arg(self):
        with mock.patch("bloat_remover.os.path.exists", return_value=True):
            with mock.patch("bloat_remover.os.name", "nt"):
                with mock.patch("bloat_remover.subprocess.run") as run_mock:
                    import bloat_remover
                    run_mock.return_value = subprocess.CompletedProcess(
                        args=[], returncode=0, stdout="SUCCESS_USER", stderr=""
                    )

                    pkg = "Microsoft.TestApp'); Write-Output 'PWNED'; #"
                    bloat_remover.BloatRemover().uninstall_uwp_app(pkg)

                    for call in run_mock.call_args_list:
                        args, _ = call
                        cmd = args[0]
                        if isinstance(cmd, list) and len(cmd) >= 7 and cmd[0] == "powershell" and cmd[5] == "-Command":
                            script = cmd[6]
                            if len(cmd) > 7:
                                pkg_arg = cmd[7]
                                if "$args[0]" in script:
                                    assert pkg_arg == pkg.replace("'", "''")
                                    assert pkg.replace("'", "''") not in script
                                    return
                    pytest.fail("No $args[0] script found in run_powershell_cmd calls")


class TestPerformanceBoosterDNSInjectionFix:
    """performance_booster.py set_dns_servers uses positional args for adapter and servers."""

    def test_dns_adapter_and_servers_passed_as_positional_args(self):
        with mock.patch("performance_booster.os.name", "nt"):
            with mock.patch("performance_booster.subprocess.run") as run_mock:
                import performance_booster
                adapter = "Ethernet'); Write-Output 'PWNED'; #"
                run_mock.side_effect = [
                    subprocess.CompletedProcess(args=[], returncode=0, stdout=adapter + "\n", stderr=""),
                    subprocess.CompletedProcess(args=[], returncode=0, stdout="", stderr=""),
                    subprocess.CompletedProcess(args=[], returncode=0, stdout="", stderr=""),
                    subprocess.CompletedProcess(args=[], returncode=0, stdout="", stderr=""),
                    subprocess.CompletedProcess(args=[], returncode=0, stdout="", stderr=""),
                ]

                booster = performance_booster.PerformanceBooster()
                booster.set_dns_servers("google")

                for call in run_mock.call_args_list:
                    args, _ = call
                    cmd = args[0]
                    if (isinstance(cmd, list) and len(cmd) >= 5 and cmd[0] == "powershell"
                            and cmd[3] == "-Command" and "Set-DnsClientServerAddress" in cmd[4]):
                        script = cmd[4]
                        adapter_arg = cmd[5]
                        servers_arg = cmd[6]

                        assert "$args[0]" in script, f"Script must use $args[0] for adapter: {script}"
                        assert "$args[1]" in script, f"Script must use $args[1] for servers: {script}"
                        assert adapter_arg == adapter
                        assert servers_arg == '"8.8.8.8", "8.8.4.4"'
                        assert adapter not in script
                        assert servers_arg not in script
                        return
                pytest.fail("Set-DnsClientServerAddress call not found")


class TestSecurityScannerSignatureInjectionFix:
    """security_scanner.py verify_signature_offline uses positional args."""

    def test_verify_signature_path_passed_as_positional_arg(self):
        with mock.patch("security_scanner.os.path.exists", return_value=True):
            with mock.patch("security_scanner.os.name", "nt"):
                with mock.patch("security_scanner.subprocess.run") as run_mock:
                    import security_scanner
                    run_mock.return_value = subprocess.CompletedProcess(
                        args=[], returncode=0,
                        stdout='{"Status":"Valid","Subject":"CN=Test"}', stderr=""
                    )

                    path = r"C:\Windows\System32\test.exe'); Write-Output 'PWNED'; #"
                    scanner = security_scanner.SecurityScanner()
                    scanner.verify_signature_offline(path)

                    for call in run_mock.call_args_list:
                        args, _ = call
                        cmd = args[0]
                        if (isinstance(cmd, list) and len(cmd) >= 5 and cmd[0] == "powershell"
                                and cmd[3] == "-Command" and "Get-AuthenticodeSignature" in cmd[4]):
                            script = cmd[4]
                            path_arg = cmd[5]

                            assert "$args[0]" in script, f"Script must use $args[0]: {script}"
                            assert path_arg == path
                            assert path not in script
                            return
                    pytest.fail("Get-AuthenticodeSignature call not found")


if __name__ == "__main__":
    pytest.main([__file__, "-v"])