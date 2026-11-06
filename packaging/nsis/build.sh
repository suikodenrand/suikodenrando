#!/usr/bin/env bash
set -euo pipefail

if (( $# != 2 )); then
  printf 'Usage: %s <directory containing both Release DLLs> <output EXE>\n' "$0" >&2
  exit 2
fi

source_dir="$(cd "$(dirname "$0")" && pwd)"
runtime_dir="$(cd "$1" && pwd)"
output_dir="$(cd "$(dirname "$2")" && pwd)"
output_exe="$output_dir/$(basename "$2")"

for file in SuikodenRandomizer.dll SuikodenRandomizer.Core.dll; do
  if [[ ! -s "$runtime_dir/$file" ]]; then
    printf 'Missing Release DLL: %s\n' "$runtime_dir/$file" >&2
    exit 1
  fi
done

"${MAKENSIS:-makensis}" -WX \
  -X'Target amd64-unicode' \
  "-DRUNTIME_DIR=$runtime_dir" \
  "-DOUTPUT_EXE=$output_exe" \
  "$source_dir/StarsOfDestiny.nsi"
