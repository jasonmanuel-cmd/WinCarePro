# WinCare Pro v2.0 — Monetization & First-Dollar Launch Guide

Everything in the codebase, offline cryptographic licensing (`WCP2.`), Store listing art, legal pages, and conversion landing page is now built and verified. Only **owner-only account steps** (signing credentials + checkout link) remain between here and collecting real revenue.

---

## Step 1: Mint Your Production Offline Signing Keypair (2 minutes)

Generate your secret ECDSA P-256 private key on your secure owner machine (store `wincare-owner-private.pem` in your password manager / encrypted backup — **never commit `.pem` to git**):

```powershell
dotnet run --project Tools\LicenseGen -c Release -- keypair --out-private C:\SecureKeys\wincare-owner-private.pem
```

Copy the printed Base64 public key into `DefaultPublicKeySpkiBase64` in `Services/LicenseService.cs`, then republish `WinCare Pro.exe`.

To issue a customer key (or batch-generate 500 keys to upload as a license-key pool in **Lemon Squeezy** or **Gumroad**!):

```powershell
dotnet run --project Tools\LicenseGen -c Release -- issue `
  --private-key C:\SecureKeys\wincare-owner-private.pem `
  --licensee "Valued Customer" `
  --edition "Founding Pro" `
  --seats 1
```

> **Tip for Zero-Server Automation:** Lemon Squeezy and Gumroad both support **"Pre-generated License Key Lists"**! Mint 500 signed `WCP2.` keys into a CSV/list, paste them into your Lemon Squeezy product once, and Lemon Squeezy will automatically email each buyer a real offline `WCP2.` key + handle global VAT/sales tax with **zero backend servers to maintain**!

---

## Step 2: Eliminate Windows SmartScreen Warnings (Choose A, B, or Both)

- **Option A — Microsoft Store (`installer\StoreListing\` + `build-msix.ps1`):**
  1. Register an individual Partner Center developer account (~$19 one-time).
  2. Reserve your app name and copy your assigned `Publisher` (`CN=...`) and `IdentityName`.
  3. Build the unsigned Store upload package from your reviewed candidate payload using `build-msix.ps1` and upload `WinCare.msix` + the 6 `1280×800` screenshots in `installer/StoreListing/`. Microsoft signs the Store package automatically!

- **Option B — Direct Website Download (`Azure Trusted Signing` / Code Signing Cert):**
  1. Sign `WinCare Pro.exe` (and `WinCare.msi`) using `sign-release.ps1` with Azure Trusted Signing (~$9.99/mo) or an OV/EV certificate so Windows SmartScreen trusts your publisher identity immediately.

---

## Step 3: Deploy `website/` & Connect Your Checkout URL (10 minutes)

1. Replace `support@wincarepro.example` in `PRIVACY.md`, `EULA.md`, and `website/` with your real support email.
2. Replace `https://checkout.wincarepro.example/...` in `website/index.html` with your Lemon Squeezy / Paddle checkout link and your signed download URL + SHA-256 hash.
3. Deploy the static `website/` folder for free on **Cloudflare Pages**, **Vercel**, **Netlify**, or **GitHub Pages**.
