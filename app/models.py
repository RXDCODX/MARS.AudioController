from __future__ import annotations

from dataclasses import dataclass, field
from datetime import datetime
from uuid import uuid4

from pydantic import BaseModel, Field


class AudioPlaybackRequest(BaseModel):
    audio_url: str = Field(alias="audioUrl")
    display_name: str | None = Field(default=None, alias="displayName")
    priority: int = 50

    model_config = {
        "populate_by_name": True,
    }


class AudioPlaybackResponse(BaseModel):
    success: bool
    message: str
    queue_position: int = Field(default=0, alias="queuePosition")
    total_in_queue: int = Field(default=0, alias="totalInQueue")

    model_config = {
        "populate_by_name": True,
    }


class AudioPlaybackStatus(BaseModel):
    is_playing: bool = Field(alias="isPlaying")
    current_audio: str | None = Field(default=None, alias="currentAudio")
    queue_length: int = Field(alias="queueLength")
    playback_progress: float = Field(alias="playbackProgress")
    current_position: str = Field(alias="currentPosition")
    total_duration: str = Field(alias="totalDuration")

    model_config = {
        "populate_by_name": True,
    }


@dataclass(slots=True)
class AudioQueueItem:
    audio_url: str
    display_name: str | None
    priority: int
    id: str = field(default_factory=lambda: str(uuid4()))
    added_at: str = field(default_factory=lambda: datetime.utcnow().isoformat() + "Z")


class MuteRequest(BaseModel):
    process_names: list[str] = Field(default_factory=list, alias="processNames")

    model_config = {
        "populate_by_name": True,
    }
