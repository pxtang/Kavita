#!/usr/bin/env bash
# Run on Linux/WSL with .NET SDK 8, Node.js 20, npm, zip and sha256sum.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
release=kavita-0.8.2-security.1-win-x64
out="$root/_output/security-backport"
mkdir -p "$out"
staging="$(mktemp -d "$out/staging.XXXXXX")"
trap 'rm -rf "$staging"' EXIT

dotnet test API.Tests/API.Tests.csproj -c Release \
    --filter 'FullyQualifiedName~TokenServiceTests|FullyQualifiedName~RefreshTokenSecurityTests'
(
    cd UI/Web
    npm ci
    NG_CLI_ANALYTICS=false npm run prod
)
mkdir -p API/wwwroot
cp -a UI/Web/dist/browser/. API/wwwroot/
dotnet publish API/API.csproj -c Release -r win-x64 --self-contained true \
    -o "$staging/Kavita"
mv "$staging/Kavita/API.exe" "$staging/Kavita/Kavita.exe"
cp -a API/I18N "$staging/Kavita/"
cp LICENSE "$staging/Kavita/LICENSE.txt"
cp INSTALL.txt "$staging/Kavita/README.txt"
cp SECURITY-BACKPORT.md "$staging/Kavita/SECURITY-BACKPORT.txt"
# Ship only the original template, never a local database or modified configuration.
rm -rf "$staging/Kavita/config"
mkdir -p "$staging/Kavita/config"
cat > "$staging/Kavita/config/appsettings.json" <<'JSON'
{
  "TokenKey": "super secret unguessable key that is longer because we require it",
  "Port": 5000,
  "IpAddresses": "",
  "BaseUrl": "/",
  "Cache": 50
}
JSON
printf '@echo off\r\npushd "%%~dp0"\r\nKavita.exe\r\npopd\r\npause\r\n' > "$staging/Kavita/Start-Kavita.cmd"
(
    cd "$staging"
    zip -qr "$staging/$release.zip" Kavita
)
mv "$staging/$release.zip" "$out/$release.zip"
(cd "$out" && sha256sum "$release.zip" > "$release.zip.sha256")
printf 'Created %s\n' "$out/$release.zip"
