#!/usr/bin/env bash
set -euo pipefail

PROJECT="NextVoiceSync.Cli/NextVoiceSync.Cli.csproj"
CONFIGURATION="${CONFIGURATION:-Release}"
OUTPUT_ROOT="${OUTPUT_ROOT:-artifacts/cli}"
DEFAULT_RIDS=(
  "linux-x64"
  "linux-arm64"
  "osx-x64"
  "osx-arm64"
)

if [ "$#" -gt 0 ]; then
  RIDS=("$@")
else
  RIDS=("${DEFAULT_RIDS[@]}")
fi

if ! command -v dotnet >/dev/null 2>&1; then
  if [ -x "${HOME}/.dotnet/dotnet" ]; then
    export DOTNET_ROOT="${HOME}/.dotnet"
    export PATH="${DOTNET_ROOT}:${DOTNET_ROOT}/tools:${PATH}"
  fi
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet コマンドが見つかりません。" >&2
  exit 1
fi

echo "Publishing NextVoiceSync.Cli as single-file binaries..."
echo "Configuration: ${CONFIGURATION}"
echo "Output root:   ${OUTPUT_ROOT}"

for rid in "${RIDS[@]}"; do
  out_dir="${OUTPUT_ROOT}/${rid}"
  echo
  echo "[${rid}] -> ${out_dir}"

  dotnet publish "${PROJECT}" \
    -c "${CONFIGURATION}" \
    -r "${rid}" \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:EnableCompressionInSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -o "${out_dir}"
done

echo
echo "Done."
echo "Examples:"
echo "  ${OUTPUT_ROOT}/linux-x64/NextVoiceSync.Cli --help"
echo "  ${OUTPUT_ROOT}/osx-arm64/NextVoiceSync.Cli --help"
