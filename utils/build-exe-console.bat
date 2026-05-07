@echo off
setlocal

cd /d "%~dp0.."

echo [1/4] Prepare virtual environment...
if not exist .venv (
  py -3 -m venv .venv
)

call .venv\Scripts\activate

echo [2/4] Install dependencies...
python -m pip install --upgrade pip
pip install -r requirements.txt
pip install pyinstaller

echo [3/4] Build EXE with console...
pyinstaller --noconfirm --clean --onefile --console --name AudioControllerPy --collect-submodules=app main.py

echo [4/4] Done. EXE path: dist\AudioControllerPy.exe
endlocal
