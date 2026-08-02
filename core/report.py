"""
WinCare Pro - Core HTML report exporter.
"""
from __future__ import annotations

from datetime import datetime

from core.platform import APP_NAME, APP_VERSION, LOG_DIR, REPORT_DIR, SEV_COLORS


class ReportExporter:
    @staticmethod
    def export_html(sysinfo, score, grade, breakdown, findings, freed_note=""):
        """Write a styled, self-contained HTML health report. Returns path."""
        stamp = datetime.now().strftime("%Y-%m-%d %H:%M")
        fname = REPORT_DIR / f"WinCare_Report_{datetime.now():%Y%m%d_%H%M%S}.html"
        color = "#2ECC71" if score >= 75 else "#F5A524" if score >= 50 else "#E5484D"
        rows = ""
        for f in findings:
            c = SEV_COLORS.get(f["severity"], "#888")
            rows += (f"<tr><td><span class='pill' style='background:{c}'>"
                     f"{f['severity']}</span></td><td>{f['category']}</td>"
                     f"<td>{f['title']}</td><td>{f['recommendation']}</td></tr>\n")
        info_rows = "".join(
            f"<tr><th>{k}</th><td>{v}</td></tr>"
            for k, v in [("Operating system", sysinfo["os"]),
                         ("Computer", sysinfo["hostname"]),
                         ("CPU", f"{sysinfo['cpu']} ({sysinfo['cores']})"),
                         ("RAM", f"{sysinfo['ram_total']} ({sysinfo['ram_used_pct']}% used)"),
                         ("System drive", f"{sysinfo['disk_total']} total, "
                                          f"{sysinfo['disk_free']} free"),
                         ("Uptime", f"{sysinfo['uptime']} (booted {sysinfo['boot_time']})")])
        deductions = "".join(f"<li>{d}</li>" for d in breakdown) or \
                     "<li>No deductions - excellent condition.</li>"
        html = f"""<!DOCTYPE html><html><head><meta charset="utf-8">
<title>{APP_NAME} Health Report</title><style>
 body{{font-family:'Segoe UI',sans-serif;background:#12151c;color:#dfe4ec;
      margin:0;padding:32px}}
 h1{{margin:0 0 4px}} .sub{{color:#8a93a6;margin-bottom:24px}}
 .score{{font-size:64px;font-weight:700;color:{color}}}
 .card{{background:#1b1f27;border:1px solid #2a3040;border-radius:12px;
       padding:20px;margin-bottom:20px}}
 table{{width:100%;border-collapse:collapse}}
 td,th{{padding:8px 10px;border-bottom:1px solid #2a3040;text-align:left;
       vertical-align:top;font-size:14px}}
 th{{color:#8a93a6;white-space:nowrap}}
 .pill{{color:#fff;padding:2px 10px;border-radius:10px;font-size:12px;
       white-space:nowrap}}
 ul{{margin:6px 0}} li{{margin:4px 0;font-size:14px}}
</style></head><body>
<h1>{APP_NAME} &mdash; System Health Report</h1>
<div class="sub">Generated {stamp} &middot; v{APP_VERSION}</div>
<div class="card"><table><tr>
 <td style="width:180px;border:none"><div class="score">{score}</div>
     <div>{grade}</div></td>
 <td style="border:none"><strong>Score deductions</strong>
     <ul>{deductions}</ul>{f"<p>{freed_note}</p>" if freed_note else ""}</td>
</tr></table></div>
<div class="card"><h3>System information</h3><table>{info_rows}</table></div>
<div class="card"><h3>Scan findings ({len(findings)})</h3>
<table><tr><th>Severity</th><th>Category</th><th>Finding</th>
<th>Recommended action</th></tr>{rows}</table></div>
<div class="card" style="color:#8a93a6;font-size:13px">
 {APP_NAME} report. Findings are advisory - review before acting.
 Logs: {LOG_DIR}</div>
</body></html>"""
        fname.write_text(html, encoding="utf-8")
        return fname
