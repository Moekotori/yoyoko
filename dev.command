#!/bin/bash
set -euo pipefail
exec /bin/bash "$(dirname "$0")/start.command" --watch
