"""
SignalR client for sending recognized speech to MARS.Server.
Handles low-latency communication for live streaming scenarios.
"""

from __future__ import annotations

import asyncio
import logging
from datetime import datetime

try:
    from signalrcore.hub_connection_builder import HubConnectionBuilder
    from signalrcore.protocol.messagepack_protocol import MessagePackHubProtocol
    SIGNALR_AVAILABLE = True
except ImportError:
    SIGNALR_AVAILABLE = False


class AudioControllerSignalRClient:
    """
    SignalR client for MARS.AudioController to communicate with MARS.Server.
    Sends recognized voice messages to VoiceRecognitionHub.
    """

    def __init__(self, server_url: str) -> None:
        """
        Initialize SignalR client.
        
        Args:
            server_url: Base URL of MARS.Server (e.g., 'http://localhost:5000')
        """
        self._logger = logging.getLogger(self.__class__.__name__)
        
        self.server_url = server_url.rstrip("/")
        self.hub_path = "/hubs/voice-recognition"
        self.hub_url = f"{self.server_url}{self.hub_path}"
        
        self._connection: HubConnection | None = None
        self._is_connected = False
        self._reconnect_attempts = 0
        self._max_reconnect_attempts = 10
        self._reconnect_delay = 2  # seconds
        
        if not SIGNALR_AVAILABLE:
            self._logger.warning("signalrcore not available, SignalR client will not work")
        
        self._logger.info("AudioControllerSignalRClient initialized for %s", self.hub_url)

    async def connect(self) -> bool:
        """
        Connect to SignalR hub.
        
        Returns:
            True if connection successful, False otherwise
        """
        if not SIGNALR_AVAILABLE:
            self._logger.error("signalrcore is not installed")
            return False
        
        try:
            self._logger.info("Attempting to connect to SignalR hub at %s", self.hub_url)
            
            # Build a fresh connection for this attempt.
            self._connection = (
                HubConnectionBuilder()
                .with_url(self.hub_url)
                .configure_logging(logging.ERROR)
                .build()
            )
            
            # Set up event handlers
            self._connection.on_open(self._on_connected)
            self._connection.on_close(self._on_disconnected)
            
            # Set up server-side method handlers
            self._connection.on("VoiceRecognitionStarted", self._on_recognition_started)
            self._connection.on("VoiceRecognitionStopped", self._on_recognition_stopped)
            
            # Start connection with timeout
            try:
                self._logger.debug("Starting SignalR connection...")
                started = await asyncio.wait_for(asyncio.to_thread(self._connection.start), timeout=15.0)
                if started:
                    self._is_connected = True
                    self._reconnect_attempts = 0

                    try:
                        await asyncio.to_thread(self._connection.invoke, "RegisterAsAudioSource", [])
                    except Exception as e:
                        self._logger.warning("Could not invoke RegisterAsAudioSource: %s", e)
                
                    self._logger.info("✓ Successfully connected to SignalR hub at %s", self.hub_url)
                    return True
                
                self._logger.error("❌ SignalR hub connection start returned failure")
                self._is_connected = False
                return False
                
            except asyncio.TimeoutError:
                self._logger.error("❌ Connection timeout to SignalR hub (timeout: 15s) - MARS.Server may be unavailable")
                self._is_connected = False
                return False
            except OSError as e:
                self._logger.error("❌ Connection refused by SignalR hub: %s (MARS.Server may be offline)", e)
                self._is_connected = False
                return False
            
        except Exception as e:
            self._logger.error("❌ Unexpected error connecting to SignalR hub: %s", e)
            import traceback
            self._logger.debug("Traceback: %s", traceback.format_exc())
            self._is_connected = False
            return False

    async def disconnect(self) -> None:
        """Disconnect from SignalR hub gracefully."""
        if not self._connection:
            return
            
        try:
            self._logger.info("Disconnecting from SignalR hub...")
            await asyncio.to_thread(self._connection.stop)
            self._is_connected = False
            self._logger.info("✓ Disconnected from SignalR hub")
        except Exception as e:
            self._logger.error("Error during disconnect: %s", e)
            self._is_connected = False

    async def send_recognized_text(
        self,
        text: str,
        language: str,
        confidence: float = 1.0,
        timestamp: str | None = None,
    ) -> bool:
        """
        Send recognized text to server.
        
        Args:
            text: Recognized text
            language: Language code (e.g., 'ru', 'en')
            confidence: Recognition confidence (0.0 to 1.0)
            timestamp: ISO format timestamp (optional)
            
        Returns:
            True if sent successfully, False otherwise
        """
        if not self._is_connected or not self._connection:
            self._logger.warning("Not connected to hub, cannot send message: %r", text[:50])
            return False
        
        try:
            # Prepare message payload as dictionary
            message = {
                "text": text,
                "language": language,
                "confidence": confidence,
                "timestamp": timestamp or datetime.utcnow().isoformat(),
            }
            
            # Send to server method: ReceiveVoiceMessage
            await asyncio.to_thread(self._connection.invoke, "ReceiveVoiceMessage", [message])
            
            self._logger.debug(
                "✓ Sent voice message: text=%r, language=%s, confidence=%.2f",
                text[:50],
                language,
                confidence,
            )
            return True
            
        except Exception as e:
            self._logger.error("Failed to send voice message: %s", e)
            return False

    async def send_vad_event(self, is_active: bool) -> bool:
        """
        Send voice activity detection event.
        
        Args:
            is_active: True if voice activity detected
            
        Returns:
            True if sent successfully
        """
        if not self._is_connected or not self._connection:
            return False
        
        try:
            await asyncio.to_thread(self._connection.invoke, "VoiceActivityDetected", [is_active])
            return True
        except Exception as e:
            self._logger.error("Failed to send VAD event: %s", e)
            return False

    def is_connected(self) -> bool:
        """Check if currently connected to hub."""
        return self._is_connected

    def get_status(self) -> dict[str, bool | str | int]:
        """Get client status information."""
        return {
            "is_connected": self._is_connected,
            "hub_url": self.hub_url,
            "reconnect_attempts": self._reconnect_attempts,
            "max_reconnect_attempts": self._max_reconnect_attempts,
        }

    def _on_connected(self) -> None:
        """Called when connected to hub."""
        self._is_connected = True
        self._logger.info("✓ SignalR connection established with hub")

    def _on_disconnected(self) -> None:
        """Called when disconnected from hub."""
        self._is_connected = False
        self._logger.warning("⚠️  SignalR connection lost - reconnection will be attempted")

    def _on_error(self, error: Exception) -> None:
        """Called when connection error occurs."""
        self._logger.error("❌ SignalR error: %s", error)
        self._reconnect_attempts += 1

    def _on_recognition_started(self) -> None:
        """Called when server starts recognition session."""
        self._logger.info("🎤 Server started voice recognition session")

    def _on_recognition_stopped(self) -> None:
        """Called when server stops recognition session."""
        self._logger.info("🛑 Server stopped voice recognition session")
