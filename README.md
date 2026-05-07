# Audio Controller (Python)

Полный перенос сервиса на Python с сохранением API-эндпоинтов.

## Технологии

- FastAPI
- Uvicorn
- pycaw/comtypes (Windows Core Audio)
- python-vlc (воспроизведение аудио)
- httpx (скачивание MP3)

## Что реализовано

- Мониторинг громкости микрофона каждые 5 минут (автоподнятие до 100%)
- Очередь воспроизведения MP3 по URL с приоритетами
- Управление воспроизведением: queue/status/skip/stop/pause/resume/volume
- Mute/Unmute аудиосессий процессов

## Запуск

```bat
run.bat
```

`run.bat` запускает сервер на `http://localhost:30691`.
Собранный `AudioControllerPy.exe` по умолчанию поднимается на `http://localhost:30695`.

Или вручную:

```bash
py -3 -m venv .venv
.venv\Scripts\activate
pip install -r requirements.txt
uvicorn main:app --host 0.0.0.0 --port 30691
```

## Эндпоинты

- `GET /`
- `POST /api/audioplayback/queue`
- `GET /api/audioplayback/status`
- `POST /api/audioplayback/skip`
- `POST /api/audioplayback/stop`
- `POST /api/audioplayback/pause`
- `POST /api/audioplayback/resume`
- `GET /api/audioplayback/queue`
- `GET /api/audioplayback/volume`
- `POST /api/audioplayback/volume?volume=80`
- `GET /api/microphone/volume`
- `POST /api/microphone/volume/max`
- `POST /api/microphone/volume/{volume}`
- `POST /api/soundbar/mute`
- `POST /api/soundbar/unmute`
- `GET /api/soundbar/bagcount`

## Важные требования

- Windows 10/11
- Установленный VLC media player (для `python-vlc`)
- Python 3.11+
