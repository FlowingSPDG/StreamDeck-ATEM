@echo off
echo PostBuild Event Started

if exist "%~dp0DistributionTool.exe" (
    echo DistributionTool.exe found
    if not exist "%~dp0..\Release" mkdir "%~dp0..\Release"
    echo Cleaning existing files...
    if exist "%~dp0..\Release\*.streamDeckPlugin" del "%~dp0..\Release\*.streamDeckPlugin"
    echo Running DistributionTool...
    "%~dp0DistributionTool.exe" -b -i "%~dp0bin\Release\dev.flowingspdg.atem.sdPlugin" -o "%~dp0..\Release"
    if errorlevel 1 (
        echo DistributionTool failed with error code %errorlevel%
        exit /b 1
    ) else (
        echo DistributionTool completed successfully
        exit /b 0
    )
) else (
    echo DistributionTool.exe not found
    exit /b 1
)
