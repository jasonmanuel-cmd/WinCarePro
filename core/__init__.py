# -*- coding: utf-8 -*-
"""WinCare Pro - core engine package (non-GUI business logic).

Splitting the monolithic main.py into this package keeps the command-line
tools, the engines, and the GUI clearly separated. Nothing in this package
imports main.py, so there is no circular-dependency risk.
"""
