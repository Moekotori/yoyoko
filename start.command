#!/bin/bash
set -euo pipefail

cd "$(dirname "$0")"
# Finder 启动时不一定继承终端的开发工具路径。
export PATH="$PATH:$HOME/.dotnet:$HOME/.cargo/bin:/opt/homebrew/bin:/usr/local/bin"

server_pid=""
client_pid=""
cleanup() {
  if [[ -n "$client_pid" ]]; then kill "$client_pid" 2>/dev/null || true; fi
  if [[ -n "$server_pid" ]]; then kill "$server_pid" 2>/dev/null || true; fi
}
on_exit() {
  local status=$?
  cleanup
  if [[ "$status" -ne 0 && "$status" -ne 130 && "$status" -ne 143 && -t 0 ]]; then
    printf '\n启动失败，请查看上面的错误。按回车关闭。\n'
    read -r _ || true
  fi
}
trap on_exit EXIT
trap 'exit 130' INT
trap 'exit 143' TERM HUP

for tool in dotnet curl; do
  if ! command -v "$tool" >/dev/null 2>&1; then
    printf '缺少工具：%s。请先安装。\n' "$tool" >&2
    exit 1
  fi
done

printf '正在构建桌面客户端…\n'
dotnet build src/client/App/Chat.App.csproj -c Release --nologo

origin="http://localhost:8080"
discovery=$(curl --noproxy '*' -fsS --max-time 2 "$origin/.well-known/lightchat" 2>/dev/null || true)
if [[ "$discovery" == *'"instance_id"'* && "$discovery" == *'"protocol_version"'* && "$discovery" == *'"gateway"'* ]]; then
  printf '复用已运行的本地服务：%s\n' "$origin"
else
  if ! command -v cargo >/dev/null 2>&1; then
    printf '缺少 cargo，请先安装 Rust。\n' >&2
    exit 1
  fi
  printf '正在构建本地服务端…\n'
  cargo build --locked -p chat-server
  CHAT_CONFIG="$PWD/config.toml" \
    CHAT__SERVER__LISTEN=127.0.0.1:8080 \
    CHAT__SERVER__PUBLIC_URL="$origin" \
    CHAT__SERVER__ALLOWED_ORIGINS="$origin" \
    CHAT__STORAGE__PUBLIC_URL="$origin/api/v1" \
    "${CARGO_TARGET_DIR:-target}/debug/chat-server" &
  server_pid=$!
  ready=false
  for ((attempt=0; attempt<60; attempt++)); do
    if ! kill -0 "$server_pid" 2>/dev/null; then
      printf '服务端已退出，请查看错误或检查 8080 端口是否被占用。\n' >&2
      exit 1
    fi
    if curl --noproxy '*' -fsS --max-time 1 "$origin/health/live" >/dev/null 2>&1; then
      ready=true
      break
    fi
    sleep 0.5
  done
  if [[ "$ready" != true ]]; then
    printf '等待本地服务启动超时。\n' >&2
    exit 1
  fi
fi

printf '\n正在打开客户端。在界面中添加实例：%s\n关闭窗口或按 Ctrl+C 结束。\n\n' "$origin"
dotnet src/client/App/bin/Release/net10.0/Chat.App.dll &
client_pid=$!
wait "$client_pid"
client_pid=""
