@echo off
rem cleaning build output and publish directories

if exist "bin" (
    echo removing bin...
    rmdir /s /q "bin"
)

if exist "obj" (
    echo removing obj...
    rmdir /s /q "obj"
)

if exist "publish" (
    echo removing publish...
    rmdir /s /q "publish"
)

echo clean completed.
pause