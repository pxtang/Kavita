# Kavita 0.8.2-security.1 for Windows x64

This is an unofficial backport for GHSA-m2v3-fcjh-hm22 / CVE-2026-47202,
based on upstream tag v0.8.2, commit c850f49cd4429a1d2fc470e0be45a9b44a72f0ef.
It uses assembly version 0.8.2.1 to distinguish it from the original build.
No database migrations or frontend behavior were changed.

The advisory lists 0.9.0.2 as the patched release; 0.8.9.1 was a tested vulnerable
version. The source change was found in upstream commit
5e556f20b681188ee3f30ed078ac5a463f861c1b (May 19, 2026).
The refresh compatibility correction from
8d053f05598f42c680ece2be7071df32494c8004 (September 1, 2026) is included:
a signed JWT can be expired, but a valid refresh token is always required,
and the user is selected by the signed user ID.

References:
- https://github.com/Kareadita/Kavita/security/advisories/GHSA-m2v3-fcjh-hm22
- https://github.com/Kareadita/Kavita/commit/5e556f20b681188ee3f30ed078ac5a463f861c1b
- https://github.com/Kareadita/Kavita/commit/8d053f05598f42c680ece2be7071df32494c8004

## What changed

Refresh requests now verify the server's JWT signature and require the HS512
algorithm used by this version of Kavita before trusting the user ID. Invalid
refresh tokens are always rejected, irrespective of JWT expiry. The original
Identity token provider and its lifetime are retained; an expired refresh token
requires a new login. Password login and API-key login are unchanged.

This fixes the linked advisory only. Restore reports known vulnerabilities in
other original 0.8.2 dependencies. This package is not an upstream-supported
release or a general security update; use a maintained release when possible.

## Install over an existing 0.8.2 installation

1. Stop Kavita, including any Windows service or scheduled task that starts it.
2. Back up the entire existing Kavita directory, including its complete config
   directory. Keep this backup outside both the old and new installation folders.
3. Extract the ZIP to a NEW writable folder, for example C:\Kavita-patched.
   The executable is in the extracted Kavita subfolder. Do not use Program Files.
4. Replace the new folder's template config directory with a COPY of the entire
   old config directory. This preserves the database, settings, covers, bookmarks,
   and other server data. Do not overwrite the old config with the template.
5. Run Start-Kavita.cmd from the new Kavita folder, then open
   http://localhost:5000 (or the existing configured port/base URL). Windows x64
   is required. The .NET runtime is bundled; no separate .NET install is needed.
6. Verify login, users/libraries, reading progress, covers, and a sample book.
   If you use a Windows service, update its executable path to the new Kavita.exe
   and set its working directory to the new Kavita folder.
7. Keep the stopped old installation and backup until verification is complete.
   To roll back, stop the patched instance and restore the backed-up installation.
   Do not run both copies against the same database.

For a fresh installation, keep the supplied template config and launch normally.

If compromise may already have occurred, this patch does not undo it. Rotate the
TokenKey in config/appsettings.json while stopped (this invalidates existing JWTs),
reset user passwords/API keys as appropriate, and review users/admin access.

## Build from the accompanying source

On Linux/WSL, install .NET SDK 8, Node.js 20, npm, zip and sha256sum. From the
source root, run:

    bash scripts/build-windows-backport.sh

The script runs the token regression tests, builds the original Angular UI using
package-lock.json, publishes the server self-contained for win-x64, and creates
_output/security-backport/kavita-0.8.2-security.1-win-x64.zip plus its SHA-256 file.
The package includes the original GPLv3 license. Provide the accompanying source
archive when distributing the binary. Build dependencies are restored from their
usual public registries. The source archive includes everything needed to run this build script.
