#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export VELVET_DEVELOPMENT=1
export VELVET_DATA_DIR="${VELVET_DATA_DIR:-$PWD/.data}"
exec dotnet run --project src/VelvetAgent.Service
