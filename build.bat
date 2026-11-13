@echo off
echo ============================================
echo  Open Forge — Build Script
echo ============================================

echo [1/3] Installing dependencies...
pip install -r requirements.txt --break-system-packages
pip install pyinstaller --break-system-packages

echo [2/3] Building EXE with PyInstaller...
pyinstaller ^
  --onefile ^
  --windowed ^
  --name "OpenForge" ^
  --icon assets\icon.ico ^
  --add-data "models_db;models_db" ^
  --add-data "assets;assets" ^
  main.py

echo [3/3] Done! Find OpenForge.exe in the dist\ folder.
pause
