from __future__ import annotations

import asyncio
import logging

from pycaw.pycaw import AudioUtilities


class SoundBarService:
    def __init__(self) -> None:
        self._muted_bag: list[tuple[int, str]] = []
        self._lock = asyncio.Lock()
        self._logger = logging.getLogger(self.__class__.__name__)

    async def mute_all(self, process_names: list[str]) -> None:
        self._logger.info("MuteAll started. Excluded processes: %s", process_names)
        muted_count = 0
        async with self._lock:
            sessions = AudioUtilities.GetAllSessions()
            for session in sessions:
                process_name = self._get_process_name(session)
                if process_name and not self._is_excluded(process_name, process_names):
                    volume = session.SimpleAudioVolume
                    volume.SetMute(1, None)
                    self._muted_bag.append((session.ProcessId, process_name))
                    muted_count += 1

        self._logger.info("MuteAll completed. Muted sessions: %s", muted_count)

    async def unmute_all(self) -> None:
        self._logger.info("UnMuteAll started. Sessions in muted bag: %s", len(self._muted_bag))
        unmuted_count = 0
        async with self._lock:
            sessions = AudioUtilities.GetAllSessions()
            muted_ids = {item[0] for item in self._muted_bag}

            for session in sessions:
                if session.ProcessId in muted_ids:
                    session.SimpleAudioVolume.SetMute(0, None)
                    unmuted_count += 1

            self._muted_bag.clear()

        self._logger.info("UnMuteAll completed. Unmuted sessions: %s", unmuted_count)

    def get_bag_count(self) -> str:
        if not self._muted_bag:
            return ""

        lines = [f"({process_id}) {name}" for process_id, name in self._muted_bag]
        return "\n".join(lines)

    @staticmethod
    def _is_excluded(process_name: str, excluded_names: list[str]) -> bool:
        return any(name.lower() in process_name.lower() for name in excluded_names)

    @staticmethod
    def _get_process_name(session) -> str:
        process = session.Process
        if process is None:
            return "System"

        process_name = getattr(process, "name", None)
        if callable(process_name):
            return process_name()

        fallback_name = getattr(process, "Name", None)
        if isinstance(fallback_name, str) and fallback_name.strip():
            return fallback_name

        return "Unknown"
