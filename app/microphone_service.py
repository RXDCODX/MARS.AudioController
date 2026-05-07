from __future__ import annotations

from ctypes import POINTER, cast
import logging

from comtypes import CLSCTX_ALL
from pycaw.pycaw import AudioUtilities, IAudioEndpointVolume


class MicrophoneService:
    def __init__(self) -> None:
        self._logger = logging.getLogger(self.__class__.__name__)

    def get_microphone_volume(self) -> dict[str, float | bool | str]:
        endpoint_volume, device_name = self._get_endpoint_volume()
        current_volume = endpoint_volume.GetMasterVolumeLevelScalar()
        is_muted = endpoint_volume.GetMute() == 1

        result = {
            "volume": current_volume,
            "volumePercent": f"{current_volume:.0%}",
            "isMuted": is_muted,
            "isAtMaximum": current_volume >= 1.0,
            "deviceName": device_name,
        }
        self._logger.info(
            "Microphone volume read: volume=%s muted=%s device=%s",
            f"{current_volume:.0%}",
            is_muted,
            device_name,
        )
        return result

    def set_microphone_volume_to_max(self) -> dict[str, float | str]:
        endpoint_volume, _ = self._get_endpoint_volume()
        previous_volume = endpoint_volume.GetMasterVolumeLevelScalar()

        endpoint_volume.SetMasterVolumeLevelScalar(1.0, None)
        endpoint_volume.SetMute(0, None)

        result = {
            "previousVolume": previous_volume,
            "previousVolumePercent": f"{previous_volume:.0%}",
            "currentVolume": 1.0,
            "currentVolumePercent": "100%",
            "message": "Microphone volume set to maximum",
        }
        self._logger.info("Microphone volume set to maximum (previous=%s)", f"{previous_volume:.0%}")
        return result

    def set_microphone_volume(self, volume: float) -> dict[str, float | str]:
        endpoint_volume, _ = self._get_endpoint_volume()
        previous_volume = endpoint_volume.GetMasterVolumeLevelScalar()

        endpoint_volume.SetMasterVolumeLevelScalar(volume, None)
        endpoint_volume.SetMute(0, None)

        result = {
            "previousVolume": previous_volume,
            "previousVolumePercent": f"{previous_volume:.0%}",
            "currentVolume": volume,
            "currentVolumePercent": f"{volume:.0%}",
            "message": f"Microphone volume set to {volume:.0%}",
        }
        self._logger.info(
            "Microphone volume changed from %s to %s",
            f"{previous_volume:.0%}",
            f"{volume:.0%}",
        )
        return result

    def ensure_max_volume(self) -> float:
        endpoint_volume, _ = self._get_endpoint_volume()
        current_volume = endpoint_volume.GetMasterVolumeLevelScalar()

        if current_volume < 1.0:
            endpoint_volume.SetMasterVolumeLevelScalar(1.0, None)
            endpoint_volume.SetMute(0, None)

        return current_volume

    def _get_endpoint_volume(self):
        microphone_device = self._get_microphone_device()
        interface = microphone_device.Activate(IAudioEndpointVolume._iid_, CLSCTX_ALL, None)
        endpoint_volume = cast(interface, POINTER(IAudioEndpointVolume))

        # FriendlyName may not be present on low-level COM pointer; fall back to safe value
        device_name = None
        try:
            device_name = getattr(microphone_device, "FriendlyName")
        except Exception:
            device_name = None

        if not device_name:
            try:
                if hasattr(microphone_device, "GetId"):
                    device_name = str(microphone_device.GetId())
                else:
                    device_name = "Unknown"
            except Exception:
                device_name = "Unknown"

        return endpoint_volume, device_name

    @staticmethod
    def _get_microphone_device():
        if hasattr(AudioUtilities, "GetMicrophone"):
            microphone_device = AudioUtilities.GetMicrophone()
            if microphone_device is not None:
                return microphone_device

        all_devices = AudioUtilities.GetAllDevices()
        microphone_device = next(
            (
                device
                for device in all_devices
                if "microphone" in (device.FriendlyName or "").lower()
            ),
            None,
        )

        if microphone_device is None:
            raise RuntimeError("No default microphone found")

        return microphone_device
