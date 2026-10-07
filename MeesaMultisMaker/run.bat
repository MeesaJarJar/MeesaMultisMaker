@echo off
cd /d "%~dp0"
echo Building MeesaMultisMaker...
"C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\MSBuild.exe" "MeesaMultisMaker.csproj" /p:Configuration=Debug /verbosity:minimal
if %errorlevel% neq 0 (
    echo Build failed!
    pause
    exit /b 1
)
echo Build succeeded. Launching...
start "" "bin\Debug\MeesaMultisMaker.exe"
