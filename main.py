from __future__ import annotations

import os
import logging
import sys

from fastapi import FastAPI, HTTPException, Query
import uvicorn

from app.microphone_service import MicrophoneService
from app.models import AudioPlaybackRequest, MuteRequest
from app.monitor_service import MicrophoneVolumeMonitorService
from app.playback_service import AudioPlaybackQueueService
from app.soundbar_service import SoundBarService

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s %(levelname)s [%(name)s] %(message)s",
    stream=sys.stdout,
    force=True,
)
logger = logging.getLogger("AudioController")

DEVELOPMENT_PORT = 30691
PRODUCTION_PORT = 30695


def is_production_runtime() -> bool:
    environment_name = os.getenv("AUDIO_CONTROLLER_ENV", "").strip().lower()
    if environment_name == "production" or environment_name == "prod":
        return True

    if environment_name == "development" or environment_name == "dev":
        return False

    return bool(getattr(sys, "frozen", False))


def get_default_port() -> int:
    port_text = os.getenv("AUDIO_CONTROLLER_PORT", "").strip()
    if port_text:
        try:
            return int(port_text)
        except ValueError:
            logger.warning("Invalid AUDIO_CONTROLLER_PORT value %r, using default port", port_text)

    if is_production_runtime():
        return PRODUCTION_PORT

    return DEVELOPMENT_PORT

app = FastAPI(title="Audio Controller REST Server")


@app.on_event("startup")
async def startup() -> None:
    logger.info("Starting Audio Controller services")
    app.state.playback_service = AudioPlaybackQueueService()
    app.state.microphone_service = MicrophoneService()
    app.state.soundbar_service = SoundBarService()
    app.state.monitor_service = MicrophoneVolumeMonitorService(app.state.microphone_service)

    await app.state.monitor_service.start()
    logger.info("Audio Controller services started")


@app.on_event("shutdown")
async def shutdown() -> None:
    logger.info("Stopping Audio Controller services")
    await app.state.monitor_service.stop()
    await app.state.playback_service.dispose()
    logger.info("Audio Controller services stopped")


@app.get("/")
async def root() -> str:
    logger.info("GET /")
    return "Audio Controller REST Server is running!"


@app.post("/api/audioplayback/queue")
async def queue_audio(request: AudioPlaybackRequest):
    logger.info("POST /api/audioplayback/queue audioUrl=%s priority=%s", request.audio_url, request.priority)
    try:
        response = await app.state.playback_service.queue_audio_async(request)
        if response.success:
            return response.model_dump(by_alias=True)

        raise HTTPException(status_code=400, detail=response.model_dump(by_alias=True))
    except HTTPException:
        raise
    except Exception as ex:
        raise HTTPException(status_code=400, detail={"success": False, "message": str(ex)}) from ex


@app.get("/api/audioplayback/status")
async def get_status():
    logger.info("GET /api/audioplayback/status")
    try:
        status = app.state.playback_service.get_status()
        return status.model_dump(by_alias=True)
    except Exception as ex:
        raise HTTPException(status_code=400, detail={"success": False, "message": str(ex)}) from ex


@app.post("/api/audioplayback/skip")
async def skip_audio():
    logger.info("POST /api/audioplayback/skip")
    try:
        await app.state.playback_service.skip_current_async()
        return {"success": True, "message": "Skipped to next audio"}
    except Exception as ex:
        raise HTTPException(status_code=400, detail={"success": False, "message": str(ex)}) from ex


@app.post("/api/audioplayback/stop")
async def stop_audio():
    logger.info("POST /api/audioplayback/stop")
    try:
        await app.state.playback_service.stop_and_clear_async()
        return {"success": True, "message": "Playback stopped and queue cleared"}
    except Exception as ex:
        raise HTTPException(status_code=400, detail={"success": False, "message": str(ex)}) from ex


@app.post("/api/audioplayback/pause")
async def pause_audio():
    logger.info("POST /api/audioplayback/pause")
    try:
        app.state.playback_service.pause()
        return {"success": True, "message": "Playback paused"}
    except Exception as ex:
        raise HTTPException(status_code=400, detail={"success": False, "message": str(ex)}) from ex


@app.post("/api/audioplayback/resume")
async def resume_audio():
    logger.info("POST /api/audioplayback/resume")
    try:
        app.state.playback_service.resume()
        return {"success": True, "message": "Playback resumed"}
    except Exception as ex:
        raise HTTPException(status_code=400, detail={"success": False, "message": str(ex)}) from ex


@app.get("/api/audioplayback/queue")
async def get_queue_items():
    logger.info("GET /api/audioplayback/queue")
    try:
        queue_items = app.state.playback_service.get_queue_items()
        return {"success": True, "queueItems": queue_items, "count": len(queue_items)}
    except Exception as ex:
        raise HTTPException(status_code=400, detail={"success": False, "message": str(ex)}) from ex


@app.get("/api/audioplayback/volume")
async def get_volume():
    logger.info("GET /api/audioplayback/volume")
    try:
        volume = app.state.playback_service.get_volume()
        return {"success": True, "volume": volume}
    except Exception as ex:
        raise HTTPException(status_code=400, detail={"success": False, "message": str(ex)}) from ex


@app.post("/api/audioplayback/volume")
async def set_volume(volume: int = Query(default=100)):
    logger.info("POST /api/audioplayback/volume?volume=%s", volume)
    if volume < 0 or volume > 100:
        raise HTTPException(status_code=400, detail={"success": False, "message": "Volume must be between 0 and 100"})

    try:
        app.state.playback_service.set_volume(volume)
        return {"success": True, "message": f"Volume set to {volume}%", "volume": volume}
    except Exception as ex:
        raise HTTPException(status_code=400, detail={"success": False, "message": str(ex)}) from ex


@app.get("/api/microphone/volume")
async def get_microphone_volume():
    logger.info("GET /api/microphone/volume")
    try:
        return app.state.microphone_service.get_microphone_volume()
    except RuntimeError as ex:
        raise HTTPException(status_code=404, detail=str(ex)) from ex
    except Exception as ex:
        logger.exception("Failed to get microphone volume")
        raise HTTPException(status_code=500, detail="Failed to get microphone volume") from ex


@app.post("/api/microphone/volume/max")
async def set_microphone_volume_to_max():
    logger.info("POST /api/microphone/volume/max")
    try:
        return app.state.microphone_service.set_microphone_volume_to_max()
    except RuntimeError as ex:
        raise HTTPException(status_code=404, detail=str(ex)) from ex
    except Exception as ex:
        logger.exception("Failed to set microphone volume to maximum")
        raise HTTPException(status_code=500, detail="Failed to set microphone volume to maximum") from ex


@app.post("/api/microphone/volume/{volume}")
async def set_microphone_volume(volume: float):
    logger.info("POST /api/microphone/volume/%s", volume)
    if volume < 0.0 or volume > 1.0:
        raise HTTPException(status_code=400, detail="Volume must be between 0.0 and 1.0")

    try:
        return app.state.microphone_service.set_microphone_volume(volume)
    except RuntimeError as ex:
        raise HTTPException(status_code=404, detail=str(ex)) from ex
    except Exception as ex:
        logger.exception("Failed to set microphone volume")
        raise HTTPException(status_code=500, detail="Failed to set microphone volume") from ex


@app.post("/api/soundbar/mute")
async def mute_soundbar(request: MuteRequest):
    logger.info("POST /api/soundbar/mute processNamesCount=%s", len(request.process_names))
    try:
        await app.state.soundbar_service.mute_all(request.process_names)
        return {"success": True, "message": "Audio muted successfully"}
    except Exception as ex:
        raise HTTPException(status_code=400, detail={"success": False, "message": str(ex)}) from ex


@app.post("/api/soundbar/unmute")
async def unmute_soundbar():
    logger.info("POST /api/soundbar/unmute")
    try:
        await app.state.soundbar_service.unmute_all()
        return {"success": True, "message": "Audio unmuted successfully"}
    except Exception as ex:
        raise HTTPException(status_code=400, detail={"success": False, "message": str(ex)}) from ex


@app.get("/api/soundbar/bagcount")
async def get_soundbar_bag_count():
    logger.info("GET /api/soundbar/bagcount")
    try:
        bag_count = app.state.soundbar_service.get_bag_count()
        return {"success": True, "bagCount": bag_count}
    except Exception as ex:
        raise HTTPException(status_code=400, detail={"success": False, "message": str(ex)}) from ex


if __name__ == "__main__":
    # Pass the app instance directly to avoid module import issues when
    # running from a PyInstaller-built executable.
    uvicorn.run(app, host="localhost", port=get_default_port(), log_level="info", access_log=True)
