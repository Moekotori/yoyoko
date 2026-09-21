@echo off
setlocal EnableExtensions
chcp 65001 >nul
cd /d "%~dp0"
set "PATH=%ProgramFiles%\Docker\Docker\resources\bin;%PATH%"

if /I "%~1"=="--down" (
  docker compose down
  exit /b %ERRORLEVEL%
)

where node >nul 2>&1
if not errorlevel 1 (
  node "%~dp0scripts\up.mjs" %*
  exit /b %ERRORLEVEL%
)

if /I "%~1"=="--lan" set "CHAT_PUBLISH=0.0.0.0"
if not defined CHAT_PUBLISH set "CHAT_PUBLISH=127.0.0.1"

docker info >nul 2>&1
if errorlevel 1 (
  if exist "%ProgramFiles%\Docker\Docker\Docker Desktop.exe" (
    echo 正在启动 Docker Desktop…
    start "" "%ProgramFiles%\Docker\Docker\Docker Desktop.exe"
    set /a _d=0
    :wait_docker
    docker info >nul 2>&1
    if not errorlevel 1 goto docker_ok
    set /a _d+=1
    if %_d% geq 40 (
      echo Docker 引擎未运行。请先打开 Docker Desktop。
      exit /b 1
    )
    ping -n 4 127.0.0.1 >nul
    goto wait_docker
  )
  echo 未找到 Docker。请安装 Docker Desktop 后执行 docker compose up -d --build
  exit /b 1
)

:docker_ok
echo 正在启动本机 Docker 服务端…
docker compose up -d --build
if errorlevel 1 exit /b 1

set /a _tries=0
:wait_ready
curl.exe -fsS -m 2 --noproxy "*" http://127.0.0.1:8080/health/ready >nul 2>&1
if not errorlevel 1 (
  echo 服务已就绪：http://localhost:8080
  echo 发现：http://localhost:8080/.well-known/lightchat
  echo 停止：docker compose down
  exit /b 0
)
set /a _tries+=1
if %_tries% geq 90 (
  echo 等待 /health/ready 超时。请执行 docker compose logs api
  exit /b 1
)
ping -n 2 127.0.0.1 >nul
goto wait_ready
