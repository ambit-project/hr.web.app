#!/usr/bin/env bash
# Inspekcja JetBrains (jb inspectcode). Zgłoszenie = kod wyjścia 1.
# Użycie: scripts/inspect-code.sh [--no-build]
set -euo pipefail
cd "$(dirname "$0")/.."

if ! command -v jb >/dev/null 2>&1; then
    echo "Brak 'jb'. Zainstaluj: dotnet tool install -g JetBrains.ReSharper.GlobalTools" >&2
    exit 1
fi

if [[ "${1:-}" != "--no-build" ]]; then
    dotnet build --nologo -v q
fi

jb inspectcode HrWebApp.slnx --no-build --format=Text --severity=SUGGESTION --output=inspect.txt >/dev/null
if [[ -s inspect.txt ]] && grep -q '^\s\+' inspect.txt; then
    cat inspect.txt
    echo "inspectcode: są zgłoszenia (inspect.txt)" >&2
    exit 1
fi
echo "inspectcode: 0 zgłoszeń"
