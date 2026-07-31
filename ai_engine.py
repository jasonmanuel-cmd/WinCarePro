#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
================================================================================
 WinCare Pro - AI Optimization & Diagnostics Intelligence Engine
================================================================================
 Integrates AI capabilities into WinCare Pro:
  1. AI System Audit & Recommendations
  2. AI Process & Service Explainer
  3. AI Software & Driver Keep-Up-To-Date Assistant (via winget & Windows Update)
  4. Local Ollama / API / Offline Rule-Based Heuristic AI Engine fallback
================================================================================
"""

import os
import sys
import json
import urllib.request
import urllib.error
import urllib.parse
import subprocess
import threading
from pathlib import Path

CREATE_NO_WINDOW = 0x08000000 if os.name == "nt" else 0


class WinCareAIEngine:
    """
    AI Engine for WinCare Pro.
    Supports Local Ollama, Cloud API (OpenAI/Gemini/OpenRouter), and
    a Built-in Rule-Based Heuristic AI model for instant offline analysis.
    """

    def __init__(self, ollama_url="http://localhost:11434", model_name="gemma3:1b"):
        parsed = urllib.parse.urlparse(ollama_url)
        if parsed.scheme != "http" or parsed.hostname not in {
                "localhost", "127.0.0.1", "::1"}:
            raise ValueError("Ollama URL must be a local HTTP endpoint.")
        self.ollama_url = ollama_url.rstrip("/")
        self.model_name = model_name
        self.api_key = os.environ.get("OPENROUTER_API_KEY") or os.environ.get("OPENAI_API_KEY")

    def is_ollama_available(self) -> bool:
        """Check if local Ollama server is responding."""
        try:
            req = urllib.request.Request(f"{self.ollama_url}/api/tags", method="GET")
            with urllib.request.urlopen(req, timeout=2) as resp:  # nosec B310
                return resp.status == 200
        except Exception:
            return False

    def query_llm(self, prompt: str, system_prompt: str = "") -> str:
        """
        Query LLM (Ollama or API). If unavailable, return None so caller falls back to Rule-Based AI.
        """
        if self.is_ollama_available():
            try:
                payload = {
                    "model": self.model_name,
                    "prompt": prompt,
                    "system": system_prompt,
                    "stream": False
                }
                data = json.dumps(payload).encode("utf-8")
                req = urllib.request.Request(
                    f"{self.ollama_url}/api/generate",
                    data=data,
                    headers={"Content-Type": "application/json"},
                    method="POST"
                )
                with urllib.request.urlopen(req, timeout=15) as resp:  # nosec B310
                    if resp.status == 200:
                        res = json.loads(resp.read().decode("utf-8"))
                        return res.get("response", "").strip()
            except Exception:
                pass

        return None

    def analyze_system(self, sys_data: dict, health_score: int, diagnostics: list, processes: dict) -> dict:
        """
        Perform complete AI diagnostic & optimization audit.
        Combines rule-based heuristics with LLM insights if available.
        """
        prompt = (
            f"Analyze Windows 11 System Diagnostics:\n"
            f"Health Score: {health_score}/100\n"
            f"Diagnostics Issues Found: {len(diagnostics)}\n"
            f"Background Bloat Processes: {len(processes.get('background_bloat', []))}\n"
            f"Suspicious Impostors: {len(processes.get('suspicious_impostors', []))}\n\n"
            f"Provide 3-5 concise bullet recommendations to optimize performance, clean unnecessary tasks, and keep software up to date."
        )

        llm_reply = self.query_llm(
            prompt,
            system_prompt="You are WinCare AI, an expert Windows 11 performance, security, and maintenance optimization advisor."
        )

        # Rule-Based Heuristic AI Engine Analysis (Always reliable)
        findings = []
        recommendations = []
        actions = []

        # 1. Health Score Evaluation
        if health_score < 70:
            findings.append(f"System Health is compromised ({health_score}/100). Multiple diagnostic warnings detected.")
            actions.append("Run Repairs tab (SFC / DISM restore) to fix system integrity.")
        elif health_score < 85:
            findings.append(f"System Health is Moderate ({health_score}/100). Minor optimization opportunities available.")
        else:
            findings.append(f"System Health is Optimal ({health_score}/100). System is running smoothly.")

        # 2. Process & Bloat Evaluation
        bloat_count = len(processes.get('background_bloat', []))
        if bloat_count > 0:
            findings.append(f"Detected {bloat_count} active background updater/bloat tasks consuming CPU & RAM.")
            recommendations.append("Apply 1-Click 'Clean Background Bloat' preset in Bloat Cleaner tab.")
            actions.append("Clean Background Bloat")

        impostor_count = len(processes.get('suspicious_impostors', []))
        if impostor_count > 0:
            findings.append(f"CRITICAL: {impostor_count} process(es) named after Windows system files running from non-system folders!")
            recommendations.append("Inspect Suspicious Impostors in Process Manager immediately.")
            actions.append("Inspect Impostors")

        # 3. Diagnostic Warnings
        crit_diag = [d for d in diagnostics if d.get('severity') in ('Critical', 'Warning')]
        if crit_diag:
            findings.append(f"Found {len(crit_diag)} critical system diagnostic alerts (e.g. Event Log, Updates, Disk Pressure).")
            recommendations.append("Review Diagnostics tab and clear pending Event Log / Driver issues.")

        # 4. Printer & Unnecessary Services
        recommendations.append("If you do not print, disable Print Spooler (`spooler`) & Fax to save memory & reduce attack surface.")
        recommendations.append("Enable 'Game / Max Performance Mode' to pause Xbox services, Telemetry, and background indexers.")

        summary_text = llm_reply if llm_reply else "\n".join([f"• {f}" for f in findings])

        return {
            "summary": summary_text,
            "findings": findings,
            "recommendations": recommendations,
            "quick_actions": actions,
            "ai_mode": "Ollama LLM" if llm_reply else "WinCare Heuristic AI Engine"
        }

    def explain_item(self, item_name: str, item_type: str = "process") -> dict:
        """
        Explain what a specific process, executable, or service does,
        whether it's native Windows or 3rd party, and give a recommendation.
        """
        item_lower = item_name.lower()

        # Check against baseline knowledge
        from win_baseline import CORE_WINDOWS_PROCESSES, OPTIONAL_SERVICES, BACKGROUND_BLOAT_PROCESSES

        if item_lower in CORE_WINDOWS_PROCESSES:
            return {
                "name": item_name,
                "type": "Core System Process",
                "origin": "Microsoft Windows OS",
                "risk": "Protected / Essential",
                "description": CORE_WINDOWS_PROCESSES[item_lower],
                "action": "DO NOT TERMINATE. Required for Windows stability."
            }

        if item_lower in OPTIONAL_SERVICES or item_name in OPTIONAL_SERVICES:
            svc = OPTIONAL_SERVICES.get(item_lower) or OPTIONAL_SERVICES.get(item_name)
            return {
                "name": svc["display_name"],
                "type": f"Optional Windows Service ({svc['category']})",
                "origin": "Microsoft Windows OS",
                "risk": "Safe to Modify",
                "description": svc["description"],
                "action": f"Recommended stop: {svc['recommended_stop']}. Can be disabled in Bloat Cleaner."
            }

        if item_lower in BACKGROUND_BLOAT_PROCESSES:
            return {
                "name": item_name,
                "type": "Background Updater / Helper",
                "origin": "Third-Party Application",
                "risk": "Low / Non-Essential",
                "description": BACKGROUND_BLOAT_PROCESSES[item_lower],
                "action": "Safe to terminate and remove from startup."
            }

        return {
            "name": item_name,
            "type": "Application / Background Service",
            "origin": "User Installed / System Utility",
            "risk": "Standard",
            "description": f"Standard process or service executable ({item_name}).",
            "action": "Inspect digital signature and CPU/RAM usage."
        }

    def get_winget_updates_summary(self) -> dict:
        """
        Scan winget for available software updates and structure a keep-up-to-date plan.
        """
        try:
            p = subprocess.run(
                ["winget", "upgrade", "--include-unknown"],
                capture_output=True, text=True, timeout=20, creationflags=CREATE_NO_WINDOW
            )
            out = p.stdout or ""
            lines = [line.strip() for line in out.splitlines() if line.strip()]

            updatable = []
            parsing = False
            for line in lines:
                if "---" in line:
                    parsing = True
                    continue
                if parsing and len(line.split()) >= 3:
                    parts = line.split()
                    updatable.append({"name": parts[0], "version": parts[-2], "available": parts[-1]})

            return {
                "count": len(updatable),
                "packages": updatable[:15],
                "raw_output": out
            }
        except Exception as e:
            return {"count": 0, "packages": [], "raw_output": f"winget check failed: {e}"}
