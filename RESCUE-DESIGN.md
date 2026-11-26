# Rescue UI contract (extension of the existing WPF theme)

Target: older-PC users reviewing resource demand, Defender availability and application preferences. Preserve Themes/Theme.xaml: Segoe UI; #0F766E primary, #EEF2F7 ground, white surfaces, #0F172A text. No new theme, brand, dependencies, animated transitions or blanket decorative cards.

Routes: Memory & Apps, Defender, Options in the existing scrollable sidebar. Native buttons, 14px wrapped explanatory text, spacious 20px surface padding, visible focus style. Manual sampling only for new readers; empty/not-read, busy/cancelled, partial/unknown/error and safe-mode-disabled states must remain explicit. Memory is RAM, not storage; working sets are not additive due to shared pages. No unsupported performance claims.

Quick/Full scan controls require active status, explicit named confirmation, bounded provider preflight and native safe-mode guards. Lifecycle refresh is manual and distinguishes request, correlated running/completed/cancelled, failed and unknown; events do not prove caller ownership. Graceful app close is per-row, confirmed, and limited to verified signed machine-installed VLC in the same user/session; every other process stays manual. Scan and close guards run again at the native boundary. Windows Security handoff requires confirmation and remains the remediation workflow; no in-app arbitrary deletion. Options cannot weaken confirmation or activate scheduler. Static UI uses no transitions.

Evidence gate: actual WPF renders at existing 1180x760 and minimum 980x650; fixture states labeled. Native keyboard/modal/DPI/admin/VM checks remain open. The contract is not rendered evidence.
