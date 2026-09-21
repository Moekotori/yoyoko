@echo off
setlocal EnableExtensions
chcp 65001 >nul
cd /d "%~dp0"
rem Explorer 双击时不一定带上用户安装的 dotnet。
set "PATH=%ProgramFiles%\Docker\Docker\resources\bin;%USERPROFILE%\.dotnet;%USERPROFILE%\.cargo\bin;%ProgramFiles%\dotnet;%ProgramFiles(x86)%\dotnet;%LOCALAPPDATA%\Microsoft\dotnet;%LOCALAPPDATA%\dotnet-sdk;%PATH%"
if not defined CHAT_DEFAULT_INSTANCE_URL set "CHAT_DEFAULT_INSTANCE_URL=http://localhost:8080"

set "MODE=release"
if "%~1"=="" goto :args_ok
if /I "%~1"=="--watch" (
  set "MODE=--watch"
  goto :args_ok
)
echo 用法：start.cmd [--watch]
call :maybe_pause
exit /b 1

:args_ok
call :find_dotnet
if errorlevel 1 (
  echo 未找到 .NET SDK 10，正在下载并安装到用户目录…
  call :install_dotnet
  if errorlevel 1 goto :fail
  call :find_dotnet
  if errorlevel 1 (
    echo 安装完成后仍未找到 .NET SDK 10。
    goto :fail
  )
)

call :ensure_server
if errorlevel 1 goto :fail
call :ensure_rtc

if /I "%MODE%"=="release" (
  echo 正在构建桌面客户端…
  "%DOTNET%" build src\client\App\Chat.App.csproj -c Release --nologo
  if errorlevel 1 goto :fail
  if not exist "src\client\App\bin\Release\net10.0\Chat.App.dll" (
    echo 未找到构建输出：src\client\App\bin\Release\net10.0\Chat.App.dll
    goto :fail
  )
)

echo.
echo 正在打开客户端。设置中输入 localhost 即连接本机内网服务 http://localhost:8080
if /I "%MODE%"=="--watch" (
  echo 开发模式：保存 XAML 实时刷新，C# 修改热重载或自动重启。
  echo 按 Ctrl+C 停止监听。
  echo.
  set "DOTNET_WATCH_RESTART_ON_RUDE_EDIT=1"
  "%DOTNET%" watch --non-interactive --project src\client\App\Chat.App.csproj run --configuration Debug
) else (
  echo 关闭窗口或按 Ctrl+C 结束。
  echo.
  "%DOTNET%" "src\client\App\bin\Release\net10.0\Chat.App.dll"
)
if errorlevel 1 goto :fail
exit /b 0

:fail
echo.
echo 启动失败，请查看上面的错误。
call :maybe_pause
exit /b 1

:maybe_pause
echo %cmdcmdline% | findstr /i /c:" /c " >nul
if not errorlevel 1 pause
exit /b 0

:find_dotnet
set "DOTNET="
call :try_dotnet "%USERPROFILE%\.dotnet\dotnet.exe" && exit /b 0
call :try_dotnet "%ProgramFiles%\dotnet\dotnet.exe" && exit /b 0
call :try_dotnet "%ProgramFiles(x86)%\dotnet\dotnet.exe" && exit /b 0
call :try_dotnet "%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe" && exit /b 0
call :try_dotnet "%LOCALAPPDATA%\dotnet-sdk\dotnet.exe" && exit /b 0
for /f "delims=" %%I in ('where dotnet 2^>nul') do (
  call :try_dotnet "%%I"
  if not errorlevel 1 exit /b 0
)
exit /b 1

:try_dotnet
if not exist "%~1" exit /b 1
"%~1" --list-sdks 2>nul | findstr /R /C:"^10\." >nul
if errorlevel 1 exit /b 1
set "DOTNET=%~1"
set "DOTNET_ROOT=%~dp1"
if "%DOTNET_ROOT:~-1%"=="\" set "DOTNET_ROOT=%DOTNET_ROOT:~0,-1%"
set "PATH=%DOTNET_ROOT%;%PATH%"
exit /b 0

:install_dotnet
set "INSTALL_DIR=%USERPROFILE%\.dotnet"
set "INSTALL_SCRIPT=%TEMP%\yoyoko-dotnet-install.ps1"
curl.exe -fsSL -o "%INSTALL_SCRIPT%" https://dot.net/v1/dotnet-install.ps1
if errorlevel 1 (
  echo 下载官方安装脚本失败，请检查网络后重试。
  exit /b 1
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%INSTALL_SCRIPT%" -Channel 10.0 -InstallDir "%INSTALL_DIR%"
if errorlevel 1 (
  echo .NET SDK 10 安装失败。
  exit /b 1
)
set "DOTNET_ROOT=%INSTALL_DIR%"
set "PATH=%INSTALL_DIR%;%PATH%"
exit /b 0

:ensure_server
curl.exe -fsS -m 1 --noproxy "*" http://127.0.0.1:8080/health/ready >nul 2>&1
if not errorlevel 1 (
  echo 本机服务已在 8080 端口运行。
  exit /b 0
)

set "LAN_IP="
for /f "delims=" %%I in ('powershell.exe -NoProfile -Command "Get-NetIPAddress -AddressFamily IPv4 ^| Where-Object { $_.IPAddress -notmatch '^(127\.|169\.254\.|198\.18\.)' -and $_.InterfaceAlias -notmatch 'WSL|Loopback|Meta|vEthernet' } ^| Select-Object -First 1 -ExpandProperty IPAddress"') do set "LAN_IP=%%I"

call :ensure_docker_server
if not errorlevel 1 exit /b 0

echo Docker 不可用，回退到本机 cargo 服务端。

set "SERVER_BIN="
if exist "target\release\chat-server.exe" set "SERVER_BIN=%CD%\target\release\chat-server.exe"
if not defined SERVER_BIN if exist "target\debug\chat-server.exe" set "SERVER_BIN=%CD%\target\debug\chat-server.exe"
if not defined SERVER_BIN (
  echo 未找到 chat-server，正在构建…
  where cargo >nul 2>&1
  if errorlevel 1 (
    echo 缺少 cargo，无法启动本机服务端。请安装 Docker Desktop 后执行 up.cmd。
    exit /b 1
  )
  cargo build --locked -p chat-server
  if errorlevel 1 exit /b 1
  set "SERVER_BIN=%CD%\target\debug\chat-server.exe"
)
if not exist "%SERVER_BIN%" (
  echo 未找到服务端程序：%SERVER_BIN%
  exit /b 1
)

set "CHAT__SERVER__LISTEN=0.0.0.0:8080"
set "CHAT__SERVER__PUBLIC_URL=http://localhost:8080"
set "CHAT__STORAGE__PUBLIC_URL=http://localhost:8080/api/v1"
set "CHAT__SERVER__ALLOWED_ORIGINS=http://localhost:8080,http://127.0.0.1:8080"
if defined LAN_IP set "CHAT__SERVER__ALLOWED_ORIGINS=%CHAT__SERVER__ALLOWED_ORIGINS%,http://%LAN_IP%:8080"

echo 正在启动本机/内网服务端…
start "yoyoko-server" /D "%CD%" cmd /k "set CHAT__SERVER__LISTEN=%CHAT__SERVER__LISTEN%&& set CHAT__SERVER__PUBLIC_URL=%CHAT__SERVER__PUBLIC_URL%&& set CHAT__STORAGE__PUBLIC_URL=%CHAT__STORAGE__PUBLIC_URL%&& set CHAT__SERVER__ALLOWED_ORIGINS=%CHAT__SERVER__ALLOWED_ORIGINS%&& "%SERVER_BIN%""

set /a _tries=0
:wait_health
curl.exe -fsS -m 1 --noproxy "*" http://127.0.0.1:8080/health/ready >nul 2>&1
if not errorlevel 1 (
  echo 本机服务已就绪：http://localhost:8080
  if defined LAN_IP echo 局域网地址：http://%LAN_IP%:8080
  exit /b 0
)
set /a _tries+=1
if %_tries% geq 30 (
  echo 等待本机服务启动超时。请查看 yoyoko-server 窗口中的错误。
  exit /b 1
)
ping -n 2 127.0.0.1 >nul
goto wait_health

:ensure_docker_server
where docker >nul 2>&1
if errorlevel 1 exit /b 1
docker info >nul 2>&1
if errorlevel 1 (
  if not exist "%ProgramFiles%\Docker\Docker\Docker Desktop.exe" exit /b 1
  echo 正在启动 Docker Desktop…
  start "" "%ProgramFiles%\Docker\Docker\Docker Desktop.exe"
  set /a _d=0
  :wait_engine
  docker info >nul 2>&1
  if not errorlevel 1 goto compose_up
  set /a _d+=1
  if %_d% geq 40 exit /b 1
  ping -n 4 127.0.0.1 >nul
  goto wait_engine
)

:compose_up
if defined LAN_IP (set "CHAT_PUBLISH=0.0.0.0") else (set "CHAT_PUBLISH=127.0.0.1")
echo 正在用 Docker 启动服务端（首次构建镜像可能需要几分钟）…
docker compose up -d
if errorlevel 1 exit /b 1

set /a _c=0
:wait_compose
curl.exe -fsS -m 2 --noproxy "*" http://127.0.0.1:8080/health/ready >nul 2>&1
if not errorlevel 1 (
  echo Docker 服务已就绪：http://localhost:8080
  if defined LAN_IP echo 局域网地址：http://%LAN_IP%:8080
  exit /b 0
)
set /a _c+=1
if %_c% geq 90 (
  echo 等待 Docker API 就绪超时。请执行 docker compose logs api
  exit /b 1
)
ping -n 2 127.0.0.1 >nul
goto wait_compose

:ensure_rtc
powershell.exe -NoProfile -Command "exit ([int](-not [bool](Get-NetTCPConnection -LocalPort 7880 -State Listen -ErrorAction SilentlyContinue)))" >nul 2>&1
if not errorlevel 1 (
  echo LiveKit 已在 7880 端口运行。
  exit /b 0
)

if exist ".tools\livekit-server.exe" (
  set "RTC_CONFIG=%CD%\deploy\livekit.host.yaml"
  set "RTC_NODE_IP="
  if defined LAN_IP (
    set "RTC_NODE_IP=--node-ip %LAN_IP%"
    echo 正在启动本机 LiveKit（局域网 %LAN_IP%）…
  ) else (
    echo 正在启动本机 LiveKit…
  )
  start "yoyoko-livekit" /D "%CD%" cmd /k ".tools\livekit-server.exe --config %RTC_CONFIG% %RTC_NODE_IP%"
  goto wait_rtc
)

where docker >nul 2>&1
if not errorlevel 1 (
  echo 正在用 Docker 启动 Redis 与 LiveKit…
  docker compose up -d redis livekit
  if errorlevel 1 (
    echo Docker Compose 启动 LiveKit 失败。语音加入会连不上媒体。
    exit /b 0
  )
  goto wait_rtc
)

echo 未找到 LiveKit：将 .tools\livekit-server.exe 放到仓库，或安装 Docker 后执行 docker compose up -d redis livekit。
echo 现在仍可文字聊天；进语音频道时音频会连接失败。
exit /b 0

:wait_rtc
set /a _rtc_tries=0
:wait_rtc_loop
powershell.exe -NoProfile -Command "exit ([int](-not [bool](Get-NetTCPConnection -LocalPort 7880 -State Listen -ErrorAction SilentlyContinue)))" >nul 2>&1
if not errorlevel 1 (
  echo LiveKit 已就绪：ws://localhost:7880
  exit /b 0
)
set /a _rtc_tries+=1
if %_rtc_tries% geq 20 (
  echo 等待 LiveKit 启动超时。请查看 yoyoko-livekit 窗口。
  exit /b 0
)
ping -n 2 127.0.0.1 >nul
goto wait_rtc_loop
