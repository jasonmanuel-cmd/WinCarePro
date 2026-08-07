from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


def test_commercial_packaging_requires_signing_and_packages_accessible_shell():
    package = (ROOT / "package.ps1").read_text(encoding="utf-8")
    installer = (ROOT / "installer" / "WinCarePro.iss").read_text(encoding="utf-8")

    assert 'if (-not $env:WINCAREPRO_SIGN_CERT_THUMBPRINT)' in package
    assert "unsigned commercial packages are refused" in package
    assert "dotnet publish .\\WinCarePro.Desktop\\WinCarePro.Desktop.csproj" in package
    assert "WINCAREPRO_CHECKOUT_URL" in package
    assert "WINCAREPRO_UPDATE_MANIFEST_URL" in package
    assert "WINCAREPRO_SIGNER_SUBJECT" in package
    assert "build\\release_config.json" in package
    assert '"/DMyAppVersion=$Version"' in package
    assert 'MyDesktopExeName "WinCarePro.Desktop.exe"' in installer
    assert 'Filename: "{app}\\{#MyDesktopExeName}"' in installer
    assert 'Source: "..\\PRIVACY.md"' in installer
    assert 'LicenseFile=..\\EULA.txt' in installer


def test_ci_security_and_wpf_checks_fail_closed():
    workflow = (ROOT / ".github" / "workflows" / "ci.yml").read_text(encoding="utf-8")

    assert "|| true" not in workflow
    assert "pip-audit -r requirements.txt" in workflow
    assert "WinCarePro.Desktop\\WinCarePro.Desktop.csproj" in workflow
