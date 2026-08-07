# Accessibility Audit: WinCarePro v1.3.0

Standard: WCAG 2.1 AA guidance for a Windows desktop application  
Date: 2026-08-02

## Summary

Status: **WPF ENTRY FLOW VERIFIED; COMMERCIAL CERTIFICATION BLOCKED**
Open critical: 0 | Open major: 1 | Open minor: 0

The installer now starts a self-contained WPF shell whose Guided Care controls
are exposed through Windows UI Automation. The shell has accessible names,
keyboard focus styling and shortcuts, polite live status, cancellation, and
high-contrast behavior. Safety Center and Undo Center still hand off to the
legacy CustomTkinter interface, so whole-product certification is not granted.

## Findings

| Severity | Criterion | Evidence | Required fix |
|---|---|---|---|
| Major | 2.1.1, 4.1.2 | Safety Center and Undo Center open the legacy canvas-based interface, whose action controls are not reliably exposed to UIA. | Move those mutation and rollback screens to native WPF controls before whole-product certification. |

## Verified

- Five UI Automation end-to-end tests pass against the WPF shell.
- The self-contained published shell launched with the bundled bridge; Refresh
  and Support Preview completed through UIA and returned a 303-character preview.
- Three static accessibility contracts pass and the Release C# build reports
  zero warnings and zero errors.

## Release decision

Whole-product accessibility certification is not granted until the two legacy
handoffs are migrated and the signed installer passes Narrator, keyboard-only,
200% scaling, and High Contrast checks in the clean Windows 11 VM.
