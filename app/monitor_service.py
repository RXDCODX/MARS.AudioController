from __future__ import annotations

import asyncio
import logging

from app.microphone_service import MicrophoneService


class MicrophoneVolumeMonitorService:
    def __init__(self, microphone_service: MicrophoneService) -> None:
        self._logger = logging.getLogger(self.__class__.__name__)
        self._microphone_service = microphone_service
        self._check_interval_seconds = 5 * 60
        self._running = False
        self._task: asyncio.Task | None = None

    async def start(self) -> None:
        if not self._running:
            self._running = True
            self._task = asyncio.create_task(self._run())
            self._logger.info("Microphone Volume Monitor Service started. Checking every 5 minutes.")

    async def stop(self) -> None:
        self._running = False
        if self._task is not None:
            self._task.cancel()
            try:
                await self._task
            except asyncio.CancelledError:
                pass

        self._logger.info("Microphone Volume Monitor Service stopped.")

    async def _run(self) -> None:
        while self._running:
            try:
                current_volume = self._microphone_service.ensure_max_volume()
                self._logger.info("Current microphone volume: %.0f%%", current_volume * 100)

                if current_volume < 1.0:
                    self._logger.warning("Microphone volume is not at maximum! Current: %.0f%%, Expected: 100%%", current_volume * 100)
                    self._logger.info("Microphone volume set to maximum")
                else:
                    self._logger.info("Microphone volume is at maximum level")

                await asyncio.sleep(self._check_interval_seconds)
            except asyncio.CancelledError:
                break
            except Exception as ex:
                self._logger.exception("Error occurred while checking microphone volume: %s", ex)
                await asyncio.sleep(60)
