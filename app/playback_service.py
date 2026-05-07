from __future__ import annotations

import asyncio
import heapq
import logging
import os
import tempfile
from dataclasses import asdict
from pathlib import Path

import httpx
import vlc

from app.models import AudioPlaybackRequest, AudioPlaybackResponse, AudioPlaybackStatus, AudioQueueItem


class AudioPlaybackQueueService:
    def __init__(self) -> None:
        self._logger = logging.getLogger(self.__class__.__name__)
        self._queue_lock = asyncio.Lock()
        self._queue_event = asyncio.Event()
        self._queue: list[tuple[int, int, AudioQueueItem]] = []
        self._sequence = 0
        self._running = True

        self._current_item: AudioQueueItem | None = None
        self._current_file_path: str | None = None
        self._player: vlc.MediaPlayer | None = None
        self._is_paused = False
        self._volume = 100
        self._skip_requested = False

        self._worker_task = asyncio.create_task(self._process_queue_loop())

    async def queue_audio_async(self, request: AudioPlaybackRequest) -> AudioPlaybackResponse:
        response = AudioPlaybackResponse(success=False, message="Audio URL is required")

        if request.audio_url.strip():
            async with self._queue_lock:
                self._sequence += 1
                item = AudioQueueItem(
                    audio_url=request.audio_url,
                    display_name=request.display_name,
                    priority=request.priority,
                )
                heapq.heappush(self._queue, (-request.priority, self._sequence, item))
                self._queue_event.set()
                queue_position = len(self._queue)

                self._logger.info("Audio queued: %s at position %s", item.display_name or item.audio_url, queue_position)

                response = AudioPlaybackResponse(
                    success=True,
                    message="Audio queued successfully",
                    queuePosition=queue_position,
                    totalInQueue=len(self._queue),
                )

        return response

    async def skip_current_async(self) -> None:
        self._skip_requested = True
        self._stop_player()
        self._queue_event.set()

    async def stop_and_clear_async(self) -> None:
        async with self._queue_lock:
            self._queue.clear()

        self._skip_requested = True
        self._stop_player()
        self._logger.info("Playback stopped and queue cleared")

    def pause(self) -> None:
        if self._player is not None:
            self._player.pause()
            self._is_paused = True
            self._logger.info("Playback paused")

    def resume(self) -> None:
        if self._player is not None:
            self._player.pause()
            self._is_paused = False
            self._logger.info("Playback resumed")

    def get_queue_items(self) -> list[dict[str, str | int | None]]:
        sorted_items = [queue_item for _, _, queue_item in sorted(self._queue, key=lambda value: (value[0], value[1]))]
        return [asdict(item) for item in sorted_items]

    def get_volume(self) -> int:
        return self._volume

    def set_volume(self, volume: int) -> None:
        bounded_volume = max(0, min(100, volume))
        self._volume = bounded_volume

        if self._player is not None:
            self._player.audio_set_volume(bounded_volume)
            self._logger.info("Volume set to %s%%", bounded_volume)
        else:
            self._logger.info("Volume set to %s%% (will apply to next playback)", bounded_volume)

    def get_status(self) -> AudioPlaybackStatus:
        is_playing = False
        playback_progress = 0.0
        current_position = "00:00:00"
        total_duration = "00:00:00"

        if self._player is not None:
            player_state = self._player.get_state()
            is_playing = player_state == vlc.State.Playing

            current_ms = max(self._player.get_time(), 0)
            total_ms = max(self._player.get_length(), 0)

            if total_ms > 0:
                playback_progress = current_ms / total_ms

            current_position = self._format_ms(current_ms)
            total_duration = self._format_ms(total_ms)

        status = AudioPlaybackStatus(
            isPlaying=is_playing,
            currentAudio=Path(self._current_file_path).name if self._current_file_path else None,
            queueLength=len(self._queue),
            playbackProgress=playback_progress,
            currentPosition=current_position,
            totalDuration=total_duration,
        )
        return status

    async def dispose(self) -> None:
        self._running = False
        self._queue_event.set()
        await self.stop_and_clear_async()

        if not self._worker_task.done():
            await self._worker_task

    async def _process_queue_loop(self) -> None:
        while self._running:
            await self._queue_event.wait()
            self._queue_event.clear()

            while self._running:
                queue_item: AudioQueueItem | None = None

                async with self._queue_lock:
                    if self._queue:
                        _, _, queue_item = heapq.heappop(self._queue)

                if queue_item is None:
                    break

                await self._play_audio_async(queue_item)

    async def _play_audio_async(self, queue_item: AudioQueueItem) -> None:
        self._current_item = queue_item
        self._skip_requested = False

        try:
            self._logger.info("Starting playback: %s", queue_item.display_name or queue_item.audio_url)
            local_path = await self._download_audio_file(queue_item.audio_url)
            self._current_file_path = local_path

            media = vlc.Media(local_path)
            self._player = vlc.MediaPlayer(media)
            self._player.audio_set_volume(self._volume)
            self._player.play()

            await asyncio.sleep(0.2)
            while self._running:
                state = self._player.get_state()

                if self._skip_requested:
                    break

                if state in (vlc.State.Ended, vlc.State.Stopped, vlc.State.Error):
                    break

                await asyncio.sleep(0.2)
        except Exception as ex:
            self._logger.exception("Error playing audio from URL %s: %s", queue_item.audio_url, ex)
        finally:
            self._stop_player()
            self._cleanup_temp_file()

    async def _download_audio_file(self, audio_url: str) -> str:
        self._logger.info("Downloading audio from: %s", audio_url)

        file_descriptor, temp_path = tempfile.mkstemp(prefix="audio_", suffix=".mp3")
        os.close(file_descriptor)

        async with httpx.AsyncClient(timeout=30.0, follow_redirects=True) as client:
            async with client.stream("GET", audio_url) as response:
                response.raise_for_status()
                with open(temp_path, "wb") as file_object:
                    async for chunk in response.aiter_bytes():
                        file_object.write(chunk)

        self._logger.info("Audio downloaded to: %s", temp_path)
        return temp_path

    def _stop_player(self) -> None:
        if self._player is not None:
            self._player.stop()
            self._player.release()
            self._player = None

        self._is_paused = False

    def _cleanup_temp_file(self) -> None:
        if self._current_file_path and os.path.exists(self._current_file_path):
            try:
                os.remove(self._current_file_path)
            except OSError as ex:
                self._logger.warning("Failed to delete temporary audio file: %s", ex)

        self._current_file_path = None
        self._current_item = None

    @staticmethod
    def _format_ms(value_ms: int) -> str:
        total_seconds = value_ms // 1000
        hours = total_seconds // 3600
        minutes = (total_seconds % 3600) // 60
        seconds = total_seconds % 60
        return f"{hours:02d}:{minutes:02d}:{seconds:02d}"
