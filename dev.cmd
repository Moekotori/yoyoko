@echo off
setlocal EnableExtensions
cd /d "%~dp0"
call "%~dp0start.cmd" --watch
exit /b %ERRORLEVEL%
