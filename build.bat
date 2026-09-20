@echo off
rem closing running launcher instances if any
taskkill /F /IM launcher.exe >nul 2>&1

rem building project and single-file publish executable

echo building zolsi.cc...
dotnet build -c Release
if %errorlevel% neq 0 (
    echo build failed.
    pause
    exit /b %errorlevel%
)

echo publishing launcher.exe with native aot...
dotnet publish -c Release
if %errorlevel% neq 0 (
    echo publish failed.
    pause
    exit /b %errorlevel%
)

rem checking for native protection and packing tools
set VMP_EXE=
if exist "%ProgramFiles%\VMProtect Ultimate\vmprotect_con.exe" set VMP_EXE="%ProgramFiles%\VMProtect Ultimate\vmprotect_con.exe"
if exist "%ProgramFiles(x86)%\VMProtect Ultimate\vmprotect_con.exe" set VMP_EXE="%ProgramFiles(x86)%\VMProtect Ultimate\vmprotect_con.exe"
where vmprotect_con.exe >nul 2>&1 && set VMP_EXE=vmprotect_con.exe

if defined VMP_EXE (
    echo applying VMProtect virtualization to publish\launcher.exe...
    %VMP_EXE% publish\launcher.exe publish\launcher.exe >nul 2>&1
    if %errorlevel% equ 0 (
        echo VMProtect applied successfully.
    ) else (
        echo VMProtect processing encountered an issue.
    )
) else (
    where upx.exe >nul 2>&1
    if %errorlevel% equ 0 (
        echo applying UPX compression to publish\launcher.exe...
        upx.exe --best publish\launcher.exe >nul 2>&1
    )
)

rem generating blacklist hash data file
powershell -NoProfile -Command "$p='publish\launcher.exe'; if (Test-Path $p) { $h=(Get-FileHash $p -Algorithm SHA256).Hash.ToLower(); $d=Get-Date; $f='hashes\hash['+$d.ToString('hh-mm tt]-[dd-MM-yyyy')+'].data'; $q='insert into blacklisted_builds (hash, notes) values ('''+$h+''', ''Build '+$d.ToString('yyyy-MM-dd hh:mm tt')+''') on conflict (hash) do nothing;'; if (!(Test-Path 'hashes')) { New-Item -ItemType Directory -Path 'hashes' | Out-Null }; [System.IO.File]::WriteAllText($f, $q) }"
echo hash data file generated in hashes folder.

echo build and publish completed successfully.
echo output: publish\launcher.exe
pause