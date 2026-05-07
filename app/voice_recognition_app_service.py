"""
Application service for voice recognition integration.
Manages the lifecycle and configuration of speech recognition features.
"""

from __future__ import annotations

import asyncio
import logging
from datetime import datetime
from typing import Callable

from app.speech_recognition_service import SpeechRecognitionService
from app.signalr_client import AudioControllerSignalRClient


class VoiceRecognitionAppService:
    """
    High-level service for voice recognition application.
    Manages speech recognition, SignalR integration, and configuration.
    """

    def __init__(self, server_url: str, languages: list[str] | None = None) -> None:
        """
        Initialize voice recognition app service.
        
        Args:
            server_url: Base URL of MARS.Server
            languages: List of languages to support (default: ['ru', 'en'])
        """
        self._logger = logging.getLogger(self.__class__.__name__)
        
        self.server_url = server_url
        self.languages = languages or ["ru", "en"]
        
        self._speech_service = SpeechRecognitionService(languages=self.languages)
        self._signalr_client = AudioControllerSignalRClient(server_url)
        
        self._is_listening = False
        self._reconnect_task = None
        self._logger.info("VoiceRecognitionAppService initialized")

    async def start_listening(self) -> bool:
        """
        Start voice recognition and connect to SignalR hub.
        
        Returns:
            True if successfully started, False otherwise
        """
        result = False
        try:
            # Set up callback for recognized text
            self._speech_service.set_recognition_callback(self._on_text_recognized)
            
            # Start speech recognition
            self._speech_service.start()
            self._is_listening = True
            
            # Start background reconnect monitor
            if self._reconnect_task is None or self._reconnect_task.done():
                self._reconnect_task = asyncio.create_task(self._monitor_connection())
            
            # Try to connect immediately, but do not block speech recognition startup.
            connected = await self._signalr_client.connect()
            if connected and self._signalr_client._connection:
                try:
                    await asyncio.to_thread(self._signalr_client._connection.invoke, "RegisterAsAudioSource", [])
                except Exception as e:
                    self._logger.warning("Could not invoke RegisterAsAudioSource: %s", e)
                result = True
            else:
                self._logger.warning("SignalR hub is not available now, speech recognition will keep running and retry")
                result = True
            
            self._logger.info("Voice recognition listening started")
            return result
            
        except Exception as e:
            self._logger.error("Error starting voice recognition: %s", e)
            return result

    async def stop_listening(self) -> None:
        """Stop voice recognition and disconnect from SignalR hub."""
        try:
            self._is_listening = False
            
            # Cancel reconnect task
            if self._reconnect_task and not self._reconnect_task.done():
                self._reconnect_task.cancel()
                try:
                    await self._reconnect_task
                except asyncio.CancelledError:
                    pass
            
            self._speech_service.stop()
            await self._signalr_client.disconnect()
            self._logger.info("Voice recognition listening stopped")
        except Exception as e:
            self._logger.error("Error stopping voice recognition: %s", e)

    def _on_text_recognized(self, text: str, language: str) -> None:
        """
        Callback when text is recognized from speech.
        
        Args:
            text: Recognized text
            language: Language code
        """
        if not text.strip():
            return
        
        # Send to server asynchronously
        asyncio.create_task(
            self._send_recognized_text(text, language)
        )

    async def _send_recognized_text(self, text: str, language: str) -> None:
        """Send recognized text to server via SignalR."""
        try:
            # Only send if connected to SignalR
            if not self._signalr_client.is_connected():
                self._logger.debug("Not connected to SignalR, queuing text for later: %r", text)
                return
            
            await self._signalr_client.send_recognized_text(
                text=text,
                language=language,
                confidence=1.0,
                timestamp=datetime.utcnow().isoformat(),
            )
        except Exception as e:
            self._logger.error("Error sending recognized text: %s", e)

    async def _monitor_connection(self) -> None:
        """
        Background task to monitor and maintain SignalR connection.
        Attempts to reconnect if connection is lost.
        """
        while self._is_listening:
            try:
                await asyncio.sleep(5)  # Check frequently while the app is running
                
                if not self._signalr_client.is_connected():
                    self._logger.warning("SignalR connection is down, attempting to reconnect...")
                    connected = await self._signalr_client.connect()
                    if connected:
                        self._logger.info("Reconnected to SignalR hub")
                        
                        # Re-register as audio source after reconnect
                        try:
                            await asyncio.to_thread(self._signalr_client._connection.invoke, "RegisterAsAudioSource", [])
                        except Exception as e:
                            self._logger.warning("Could not re-invoke RegisterAsAudioSource: %s", e)
                    else:
                        self._logger.warning("Reconnection attempt failed")
            except asyncio.CancelledError:
                self._logger.info("Connection monitor task cancelled")
                break
            except Exception as e:
                self._logger.error("Error in connection monitor: %s", e)
                await asyncio.sleep(5)  # Wait before retrying

    def get_status(self) -> dict:
        """Get current service status."""
        return {
            "is_listening": self._is_listening,
            "speech_recognition": self._speech_service.get_status(),
            "signalr_client": self._signalr_client.get_status(),
            "server_url": self.server_url,
            "languages": self.languages,
        }
