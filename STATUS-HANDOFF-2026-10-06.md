# WinCare Pro — session handoff (Tue Oct 06 2026, updated)

## Status: v1.0.0 SHIPPED. Nothing is blocked.

> This file previously claimed the `git push` was blocked on a PAT lacking
> `workflow` scope. That was already resolved and the note was never updated.
> Trust `git status` / `gh` output over this file.

## Verified current state

- `master` == `origin/master`, clean working tree, no divergence.
- Remote: https://github.com/jasonmanuel-cmd/WinCarePro (public)
- Tag `v1.0.0` present; release published and **not** a draft.
- Release assets (local hashes verified byte-for-byte against GitHub digests):
  - `WinCare.msi` — 60,489,728 B — `be6cb327…09a0`
  - `WinCarePro-1.0.0-win-x64.zip` — 60,691,934 B — `2dac4903…2770`

## Known caveats (intentional, documented in RELEASE.md §1)

- MSI is **unsigned**. Expect SmartScreen warnings.
- `.github/workflows/ci.yml` exists and the token does hold `workflow` scope.

## Gitignore note

`installer/*.zip` is now ignored. It was previously untracked-but-not-ignored,
which invited committing a 60 MB build artifact. Build output in `installer/`
is never meant to be tracked — only `installer/WinCare.wxs` is.

## If picking this up again

Do not re-read this file first. Run:

```powershell
cd C:\Users\blunt\Desktop\wincare-desktop
git status --short --branch
gh release list --repo jasonmanuel-cmd/WinCarePro
```

Those two commands are the source of truth.