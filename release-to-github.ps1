# Production publication is disabled pending approved release policy.
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$Version)
$ErrorActionPreference = 'Stop'
throw 'HOLD / NO_SHIP: publishing is disabled. Resolve native acceptance, reviewed source/payload identity, approved signing/timestamp credentials, channel/identity/runtime and final signed package review first. See RELEASE.md. No override switch is provided.'
