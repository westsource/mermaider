@echo off
setlocal

set "SCRIPT_DIR=%~dp0%"
set "PATH=%SCRIPT_DIR%node;%PATH%"

node "%SCRIPT_DIR%node_modules\@mermaid-js\mermaid-cli\src\cli.js" %*
