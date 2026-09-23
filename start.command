#!/bin/bash
set -euo pipefail

cd "$(dirname "$0")"
# Finder 启动时不一定继承终端的开发工具路径。
export PATH="$PATH:$HOME/.dotnet:$HOME/.cargo/bin:/opt/homebrew/bin:/usr/local/bin"

mode="${1:-release}"
if [[ "$mode" != release && "$mode" != --watch ]]; then
  printf '用法：./start.command [--watch]\n' >&2
  exit 1
fi

client_pid=""
server_pid=""
cleanup() {
  if [[ -n "$client_pid" ]]; then kill "$client_pid" 2>/dev/null || true; fi
  if [[ -n "$server_pid" ]]; then
    kill "$server_pid" 2>/dev/null || true
    wait "$server_pid" 2>/dev/null || true
  fi
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

for tool in dotnet; do
  if ! command -v "$tool" >/dev/null 2>&1; then
    printf '缺少工具：%s。请先安装。\n' "$tool" >&2
    exit 1
  fi
done

ready_url=http://127.0.0.1:8080/health/ready
if curl -fsS -m 2 --noproxy '*' "$ready_url" >/dev/null 2>&1; then
  printf '本机服务已就绪：http://localhost:8080\n'
elif command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1; then
  printf '正在用 Docker 启动服务端…\n'
  docker compose up -d
  for attempt in $(seq 1 90); do
    if curl -fsS -m 2 --noproxy '*' "$ready_url" >/dev/null 2>&1; then
      printf 'Docker 服务已就绪：http://localhost:8080\n'
      break
    fi
    if [[ "$attempt" == 90 ]]; then
      printf '等待 Docker 服务就绪超时。请查看 docker compose logs api。\n' >&2
      exit 1
    fi
    sleep 1
  done
else
  if ! command -v cargo >/dev/null 2>&1; then
    printf '本机服务未运行，且没有可用的 Docker 或 cargo。请安装 Docker Desktop 或 Rust。\n' >&2
    exit 1
  fi
  printf 'Docker 不可用，正在构建并启动本机服务端…\n'
  cargo build --locked -p chat-server
  ./target/debug/chat-server &
  server_pid=$!
  for attempt in $(seq 1 30); do
    if curl -fsS -m 2 --noproxy '*' "$ready_url" >/dev/null 2>&1; then
      printf '本机服务已就绪：http://localhost:8080\n'
      break
    fi
    if ! kill -0 "$server_pid" 2>/dev/null; then
      printf '本机服务端提前退出，请查看上面的错误。\n' >&2
      exit 1
    fi
    if [[ "$attempt" == 30 ]]; then
      printf '等待本机服务就绪超时，请查看上面的错误。\n' >&2
      exit 1
    fi
    sleep 1
  done
fi

if [[ "$mode" == release ]]; then
  printf '正在构建桌面客户端…\n'
  dotnet build src/client/App/Chat.App.csproj -c Release --nologo
fi

printf '\n正在打开客户端并连接配置的默认服务器…\n'
if [[ "$mode" == --watch ]]; then
  printf '开发模式：保存 XAML 实时刷新，C# 修改热重载或自动重启。\n按 Ctrl+C 停止监听。\n\n'
  DOTNET_WATCH_RESTART_ON_RUDE_EDIT=1 \
    dotnet watch --non-interactive --project src/client/App/Chat.App.csproj run --configuration Debug &
else
  printf '关闭窗口或按 Ctrl+C 结束。\n\n'
  dotnet src/client/App/bin/Release/net10.0/Chat.App.dll &
fi
client_pid=$!
wait "$client_pid"
client_pid=""
