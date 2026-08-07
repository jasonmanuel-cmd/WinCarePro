# Compliance Review - August 2, 2026

## Implemented controls

- Review and approval precede repairs; permanent file deletion has a separate
  irreversible-action confirmation and per-file audit outcomes.
- Operational records are local by default. Network boundaries are documented
  in `PRIVACY.md`; secrets are read from environment or protected local storage.
- The installer displays `EULA.txt`, requests administrator privileges, launches
  the accessible shell, and commercial packaging refuses unsigned artifacts.
- Update downloads require HTTPS, SHA-256 integrity, a valid Authenticode
  signature, and an exact configured signer subject.
- The interface provides keyboard focus, native UI Automation controls, status
  announcements, cancellation, and high-contrast behavior.

## Commercial approval gates

- Add the seller's verified legal identity, support contact, jurisdiction,
  refund policy, and checkout-specific disclosures after legal review.
- Ensure the checkout shows a notice at collection before purchase or activation
  data is submitted and links the final privacy policy.
- Execute clean Windows 11 installation, UI Automation, uninstall, UAC, storage,
  signed-update, and rollback evidence on the exact shipping installer.
- Review accessibility manually with Narrator, keyboard-only navigation, 200%
  scaling, and Windows High Contrast on the clean test machine.

This engineering review is not legal advice and does not certify compliance in
every jurisdiction. The release remains blocked until the external gates above
have recorded evidence.
