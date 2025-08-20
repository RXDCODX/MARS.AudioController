# Audio Controller

Сервис для управления аудио в системе MARS.

## Функциональность

### Мониторинг громкости микрофона

- **Автоматическая проверка**: Каждые 5 минут проверяется громкость микрофона
- **Логирование**: Все проверки записываются в лог с уровнем информации
- **Предупреждения**: Если громкость не на максимуме, выводится предупреждение

### API Endpoints

#### GET `/api/microphone/volume`

Получить текущую громкость микрофона

**Ответ:**

```json
{
  "volume": 0.8,
  "volumePercent": "80%",
  "isMuted": false,
  "isAtMaximum": false,
  "deviceName": "Microphone (Realtek Audio)"
}
```

#### POST `/api/microphone/volume/max`

Установить громкость микрофона на максимум

**Ответ:**

```json
{
  "previousVolume": 0.8,
  "previousVolumePercent": "80%",
  "currentVolume": 1.0,
  "currentVolumePercent": "100%",
  "message": "Microphone volume set to maximum"
}
```

#### POST `/api/microphone/volume/{volume}`

Установить громкость микрофона на указанное значение (0.0 - 1.0)

**Пример:** `POST /api/microphone/volume/0.75`

**Ответ:**

```json
{
  "previousVolume": 1.0,
  "previousVolumePercent": "100%",
  "currentVolume": 0.75,
  "currentVolumePercent": "75%",
  "message": "Microphone volume set to 75%"
}
```

## Запуск

```bash
dotnet run
```

Сервис автоматически начнет мониторинг громкости микрофона каждые 5 минут.

## Логи

Сервис выводит логи в консоль:

- Информация о запуске/остановке
- Текущая громкость микрофона
- Предупреждения, если громкость не на максимуме
- Ошибки при работе с аудио устройствами

## Требования

- .NET 9.0
- NAudio 2.2.1
- Windows (для работы с Core Audio API)
