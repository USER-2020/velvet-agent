#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../apps/desktop"
exec npm run tauri dev
