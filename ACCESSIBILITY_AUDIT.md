# Accessibility Audit: WinCarePro v1.3.0

Standard: WCAG 2.1 AA guidance for a Windows desktop application  
Date: 2026-07-28

## Summary

Status: **BLOCKED**  
Critical: 1 | Major: 2 | Minor: 1

The packaged application opens and responds, but CustomTkinter renders its
interactive controls on canvases that are not exposed meaningfully through
Windows UI Automation.

## Findings

| Severity | Criterion | Evidence | Required fix |
|---|---|---|---|
| Critical | 4.1.2 Name, Role, Value | UIA found 191 descendants but only the title-bar controls had names; application buttons, switches, navigation, and dialogs had no accessible names or roles. | Move interactive controls to a Windows-accessible UI toolkit/provider, or implement a complete UI Automation provider. |
| Major | 2.1.1 Keyboard | Only 10 Treeviews declared keyboard focus participation among 952 Tk widgets. Canvas-drawn buttons and switches were absent from traversal. | Make every action reachable by Tab/Shift+Tab and Enter/Space with visible focus. |
| Major | 2.4.7 Focus Visible | Canvas controls do not expose a consistent keyboard-focus indicator. | Add a high-contrast focus state after keyboard participation is implemented. |
| Minor | 1.4.3 Contrast | `gray45` helper text and several bright action-button backgrounds were below 4.5:1 with their text colors. | Updated helper text and darkened blue, green, red, and amber action palettes. |

## Verified

- Packaged window is visible, enabled, and responsive at 1920×1080.
- UIA inspection ran against fresh isolated `LOCALAPPDATA`, `APPDATA`, and
  temporary folders.
- Application startup, source GUI construction, and read-only diagnostics pass.
- A 200% CustomTkinter scaling construction completed at a requested
  1676×992 within the tested 1920×1080 display.
- Final isolated visual evidence: `artifacts/final_isolated_gui.bmp`.

## Release decision

Accessibility certification is not granted. A native accessible control layer
is the next release-blocking UI project; adding more canvas bindings would not
solve screen-reader semantics.
