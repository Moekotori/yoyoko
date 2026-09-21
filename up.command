#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
export PATH="$PATH:/usr/local/bin:/opt/homebrew/bin"

if [[ "${1:-}" == "--down" ]]; then
  exec docker compose down
fi

if command -v node >/dev/null 2>&1; then
  exec node ./scripts/up.mjs "$@"
fi

if [[ "${1:-}" == "--lan" ]]; then
  export CHAT_PUBLISH=0.0.0.0
fi
export CHAT_PUBLISH="${CHAT_PUBLISH:-127.0.0.1}"

if ! docker info >/dev/null 2>&1; then
  printf 'Docker 引擎未运行。请先打开 Docker Desktop。\n' >&2
  exit 1
fi

printf '正在启动本机 Docker 服务端…\n'
docker compose up -d --build

for _ in $(seq 1 90); do
  if curl -fsS -m 2 --noproxy '*' http://127.0.0.1:8080/health/ready >/dev/null 2>&1; then
    printf '服务已就绪：http://localhost:8080\n'
    printf '发现：http://localhost:8080/.well-known/lightchat\n'
    printf '停止：docker compose down\n'
    exit 0
  fi
  sleep 1
done

printf '等待 /health/ready 超时。请执行 docker compose logs api\n' >&2
exit 1
