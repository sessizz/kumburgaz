#!/usr/bin/env bash
# Material Symbols alt kümesini yeniden indirir.
# Kullanım: Styles/material-symbols-icons.txt dosyasına ikon adını ekle (alfabetik, satır başına bir ad),
# sonra bu betiği çalıştır: bash tools/update-icon-font.sh
set -euo pipefail
cd "$(dirname "$0")/.."
names=$(sort -u Styles/material-symbols-icons.txt | grep -v '^$' | paste -sd, -)
ua="Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36"
css=$(curl -fsS -A "$ua" "https://fonts.googleapis.com/css2?family=Material+Symbols+Outlined:opsz,wght,FILL,GRAD@22,400,0,0&icon_names=${names}&display=block")
url=$(printf '%s' "$css" | grep -oE 'https://[^)]+' | head -1)
curl -fsS -o wwwroot/fonts/material-symbols-outlined.woff2 "$url"
sort -u -o Styles/material-symbols-icons.txt Styles/material-symbols-icons.txt
ls -l wwwroot/fonts/material-symbols-outlined.woff2
