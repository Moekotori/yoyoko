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
cleanup() {
  if [[ -n "$client_pid" ]]; then kill "$client_pid" 2>/dev/null || true; fi
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
