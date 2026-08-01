"""
Performance baseline (v2) -- fast, diagnostic.
- 12 top-level dirs sandbox (keeps PowerShell spawn count low so we finish in ~5s)
- 5 warm scans + 1 cold
- Reports: total/scan, per-dir subprocess cost, 95% CI
"""
import shutil
import statistics
import subprocess
import sys
import tempfile
import time
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(REPO_ROOT))
import disk_analyzer as da_mod  # noqa: E402


def make_sandbox(n_dirs=12, n_files=2) -> Path:
    sb = Path(tempfile.mkdtemp(prefix="wcp_perf2_"))
    for i in range(n_dirs):
        d = sb / f"dir_{i:02d}"
        d.mkdir()
        for j in range(n_files):
            (d / f"f{j}.bin").write_bytes(b"x" * (1024 * (i + 1)))
    return sb


def time_scans(target, runs=5):
    inst = da_mod.DiskAnalyzer(target_drive=str(target))
    inst.scan_large_folders(limit=10)  # cold (discarded)
    ts = []
    for _ in range(runs):
        t0 = time.perf_counter()
        inst.scan_large_folders(limit=10)
        ts.append(time.perf_counter() - t0)
    return ts


def ci95(xs):
    m = statistics.mean(xs)
    sd = statistics.stdev(xs) if len(xs) > 1 else 0.0
    h = 1.96 * sd / (len(xs) ** 0.5)
    return m, (m - h, m + h)


def main():
    n_dirs = 12
    sb = make_sandbox(n_dirs=n_dirs)
    try:
        times = time_scans(sb, runs=5)
        m, (lo, hi) = ci95(times)
        # isolate a single powershell -NoProfile -Command startup cost
        t0 = time.perf_counter()
        subprocess.run(
            ["powershell", "-NoProfile", "-NonInteractive", "-Command", "1+1"],
            capture_output=True, text=True,
            creationflags=0x08000000 if sys.platform == "win32" else 0,
        )
        ps_startup = time.perf_counter() - t0

        print("=" * 60)
        print("baseline v2: scan_large_folders (POST-FIX)")
        print("=" * 60)
        print(f"  dirs in sandbox : {n_dirs}")
        print(f"  warm scans      : 5 (+1 cold discarded)")
        print(f"  mean / scan     : {m*1000:8.1f} ms")
        print(f"  min / max       : {min(times)*1000:8.1f} / {max(times)*1000:8.1f} ms")
        print(f"  95% CI (mean)   : [{lo*1000:.1f}, {hi*1000:.1f}] ms")
        print(f"  per-dir cost    : {m/n_dirs*1000:.1f} ms/dir  (1 PS subprocess each)")
        print(f"  bare PS startup : {ps_startup*1000:.1f} ms  (powershell -NoProfile -Command '1+1')")
        print(f"  -> scan is O(N) processes: {n_dirs} dirs => ~{n_dirs} PS spawns/scan")
        print()
        print("OPTIMIZATION LEVER: batch all dirs into a SINGLE PowerShell invocation")
    finally:
        shutil.rmtree(sb, ignore_errors=True)


if __name__ == "__main__":
    main()
