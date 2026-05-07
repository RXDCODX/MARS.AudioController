@echo off
setlocal

set "AUDIO_CONTROLLER_ENV=Development"
set "MARS_SERVER_URL=http://localhost:9255"
set "PORT=30691"
set "VOICE_RECOGNITION_ENABLED=0

if not exist .venv (
  py -3 -m venv .venv
)

call .venv\Scripts\activate
python -m pip install --upgrade pip
pip install -r requirements.txt

for /f "tokens=5" %%p in ('netstat -aon ^| findstr /R /C:":%PORT% .*LISTENING"') do (
  echo Killing process on port %PORT% with PID %%p
  taskkill /F /PID %%p >nul 2>&1
)

uvicorn main:app --host localhost --port %PORT%
