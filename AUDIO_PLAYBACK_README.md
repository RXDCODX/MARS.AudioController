# Audio Playback Queue Service - Документация

## Описание

Сервис управления очередью воспроизведения аудиофайлов с поддержкой:
- Загрузки MP3 файлов по URL
- Управления очередью воспроизведения (приоритеты)
- Контроля над воспроизведением (play, pause, resume, skip, stop)
- Отслеживания статуса воспроизведения

## Архитектура

### Компоненты

```
AudioPlaybackController
    ↓
IAudioPlaybackQueueService
    ├── Управление очередью
    ├── Воспроизведение (NAudio)
    ├── Скачивание файлов (HttpClient)
    └── Синхронизация (SemaphoreSlim, PriorityQueue)
```

## API Endpoints

### Queue Audio - Добавить в очередь
```
POST /api/audioplayback/queue

Request:
{
  "audioUrl": "https://example.com/audio.mp3",
  "displayName": "Friendly name",
  "priority": 50
}

Response:
{
  "success": true,
  "message": "Audio queued successfully",
  "queuePosition": 1,
  "totalInQueue": 5
}
```

### Get Status - Получить статус
```
GET /api/audioplayback/status

Response:
{
  "isPlaying": true,
  "currentAudio": "audio.mp3",
  "queueLength": 4,
  "playbackProgress": 0.45,
  "currentPosition": "00:01:23",
  "totalDuration": "00:03:45"
}
```

### Skip - Перейти к следующему
```
POST /api/audioplayback/skip

Response:
{
  "success": true,
  "message": "Skipped to next audio"
}
```

### Stop - Остановить и очистить очередь
```
POST /api/audioplayback/stop

Response:
{
  "success": true,
  "message": "Playback stopped and queue cleared"
}
```

### Pause - Паузировать
```
POST /api/audioplayback/pause

Response:
{
  "success": true,
  "message": "Playback paused"
}
```

### Resume - Продолжить
```
POST /api/audioplayback/resume

Response:
{
  "success": true,
  "message": "Playback resumed"
}
```

### Get Queue - Получить очередь
```
GET /api/audioplayback/queue

Response:
{
  "success": true,
  "queueItems": [
    {
      "audioUrl": "https://example.com/audio1.mp3",
      "displayName": "Song 1",
      "priority": 50,
      "id": "guid-1",
      "addedAt": "2026-01-25T12:00:00Z"
    },
    ...
  ],
  "count": 5
}
```

## Использование

### Регистрация в Program.cs

#### Способ 1: Простая регистрация
```csharp
builder.Services.AddAudioPlaybackQueue();
```

#### Способ 2: С конфигурацией HttpClient
```csharp
builder.Services.AddAudioPlaybackQueue(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("User-Agent", "AudioController/1.0");
});
```

### Использование в контроллере
```csharp
[ApiController]
[Route("api/[controller]")]
public class MyController(IAudioPlaybackQueueService audioService) : ControllerBase
{
    [HttpPost("play")]
    public async Task<IActionResult> PlayAudio([FromBody] AudioPlaybackRequest request)
    {
        var response = await audioService.QueueAudioAsync(request);
        return response.Success ? Ok(response) : BadRequest(response);
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        var status = audioService.GetStatus();
        return Ok(status);
    }
}
```

## Примеры curl

### Добавить в очередь
```bash
curl -X POST http://localhost:5000/api/audioplayback/queue \
  -H "Content-Type: application/json" \
  -d '{
    "audioUrl": "https://example.com/audio.mp3",
    "displayName": "My Song",
    "priority": 50
  }'
```

### Получить статус
```bash
curl http://localhost:5000/api/audioplayback/status
```

### Пропустить
```bash
curl -X POST http://localhost:5000/api/audioplayback/skip
```

### Остановить
```bash
curl -X POST http://localhost:5000/api/audioplayback/stop
```

### Пауза
```bash
curl -X POST http://localhost:5000/api/audioplayback/pause
```

### Продолжить
```bash
curl -X POST http://localhost:5000/api/audioplayback/resume
```

### Получить очередь
```bash
curl http://localhost:5000/api/audioplayback/queue
```

## Особенности

### Priority Queue
Элементы в очереди отсортированы по приоритету (0-100):
- Более высокий приоритет = раньше в очереди
- По умолчанию: 50

### Скачивание файлов
- Файлы скачиваются во временную папку (/tmp)
- Автоматически удаляются после воспроизведения
- Поддержка HTTP и HTTPS

### Синхронизация
- `SemaphoreSlim` для потокобезопасного доступа к очереди
- `PriorityQueue<T, TPriority>` для управления приоритетами
- `CancellationToken` для graceful shutdown

### Воспроизведение
- NAudio для кроссплатформенного воспроизведения
- Поддержка основного аудиоустройства системы
- Отслеживание статуса (playing, paused, stopped)

## Обработка ошибок

Все методы возвращают информацию об ошибке в response:

```csharp
public class AudioPlaybackResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } // Описание ошибки
    public int QueuePosition { get; set; }
    public int TotalInQueue { get; set; }
}
```

## Lifecycle

```
┌─ Program.cs ─┐
│              │
│ AddServices  │
└──────┬───────┘
       │
       ▼
┌──────────────────────────────────────┐
│ AudioPlaybackQueueService            │
│  - Инициализация WaveOutEvent        │
│  - Готово к использованию            │
└──────────┬───────────────────────────┘
           │
      (при запросе)
           │
           ▼
┌──────────────────────────────────────┐
│ QueueAudioAsync                      │
│  - Валидация URL                     │
│  - Добавление в PriorityQueue        │
│  - Запуск ProcessQueueAsync           │
└──────────┬───────────────────────────┘
           │
           ▼
┌──────────────────────────────────────┐
│ ProcessQueueAsync                    │
│  - Цикл по очереди                   │
│  - PlayAudioAsync для каждого        │
└──────────┬───────────────────────────┘
           │
           ▼
┌──────────────────────────────────────┐
│ PlayAudioAsync                       │
│  - DownloadAudioFileAsync            │
│  - AudioFileReader init              │
│  - WavePlayer.Init()                 │
│  - WavePlayer.Play()                 │
│  - Ожидание завершения               │
│  - Удаление временного файла         │
└──────────┬───────────────────────────┘
           │
    (при Dispose)
           │
           ▼
┌──────────────────────────────────────┐
│ DisposeAsync                         │
│  - StopAndClearAsync()               │
│  - Cleanup ресурсов                  │
│  - Dispose WavePlayer, AudioFileReader
└──────────────────────────────────────┘
```

## Производительность

- **Скачивание**: Асинхронное, параллельно с проигрыванием
- **Память**: Файлы хранятся временно, удаляются после использования
- **CPU**: Минимальное использование (только при активном воспроизведении)
- **Сеть**: Один запрос на загрузку файла

## Безопасность

- ✅ Валидация URL
- ✅ Временные файлы в безопасной папке
- ✅ Проверка успешности загрузки (EnsureSuccessStatusCode)
- ✅ Обработка исключений и логирование
- ✅ Graceful shutdown (IAsyncDisposable)

## Требования

- .NET 10
- NAudio NuGet пакет
- HttpClient (встроен в .NET)
- Поддерживаемое аудиоустройство
