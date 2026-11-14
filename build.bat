@echo off
chcp 65001 >nul
echo ============================================
echo  Open Forge - Build
echo ============================================

echo [1/3] Installing dependencies...
pip install -r requirements.txt pyinstaller || goto :fail

echo [2/3] Building OpenForge.exe...
pyinstaller --noconfirm OpenForge.spec || goto :fail

echo [3/3] Building installer (needs Inno Setup 6)...
set ISCC="%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if exist %ISCC% (
  %ISCC% installer.iss || goto :fail
  echo Done: dist\OpenForge.exe and dist\OpenForge-setup.exe
) else (
  echo Inno Setup not found - only dist\OpenForge.exe was built.
)
pause
exit /b 0

:fail
echo Build failed.
pause
exit /b 1
