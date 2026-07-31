#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
================================================================================
 WinCare Pro - System Tray Background Auto-Maintenance Worker
================================================================================
 Silent background agent that monitors system resources, auto-flushes RAM
 when memory pressure exceeds 80%, and provides 1-click system tray controls.
================================================================================
"""

import os
import sys
import time
import threading
import subprocess
from pathlib import Path

import psutil
from performance_booster import PerformanceBooster

CREATE_NO_WINDOW = 0x08000000 if os.name == "nt" else 0


class WinCareTrayWorker:
    """
    Background RAM & Resource Auto-Maintenance Worker.
    """

    def __init__(self, ram_threshold_pct=80.0):
        self.ram_threshold_pct = ram_threshold_pct
        self.running = False
        self.booster = PerformanceBooster()

    def start_background_monitoring(self, poll_interval_sec=60):
        """Start silent background thread monitoring RAM pressure."""
        self.running = True

        def loop():
            while self.running:
                try:
                    ram_used = psutil.virtual_memory().percent
                    if ram_used >= self.ram_threshold_pct:
                        # Auto-trim RAM standby list when memory pressure is high
                        self.booster.flush_ram_standby_list()
                except Exception:
                    pass
                time.sleep(poll_interval_sec)

        t = threading.Thread(target=loop, daemon=True)
        t.start()
        return t

    def stop(self):
        """Stop background worker loop."""
        self.running = False


if __name__ == "__main__":
    worker = WinCareTrayWorker(ram_threshold_pct=80)
    worker.start_background_monitoring(poll_interval_sec=5)
    print("WinCare Tray Auto-Maintenance Worker started in background...")
    time.sleep(2)
    worker.stop()
    print("Worker stopped.")
