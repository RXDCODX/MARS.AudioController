"""
Speech recognition service using Silero VAD + OpenAI Whisper.
Optimized for low-latency real-time speech recognition for live streaming.
"""

from __future__ import annotations

import asyncio
import logging
import threading
from typing import Callable

import numpy as np
import sounddevice as sd
import torch
import torchaudio

# For speech-to-text, we'll use faster-whisper for speed optimization
try:
    from faster_whisper import WhisperModel
    WHISPER_AVAILABLE = True
except ImportError:
    WHISPER_AVAILABLE = False

# As fallback, prepare for speech_recognition library
try:
    import speech_recognition as sr
    SR_AVAILABLE = True
except ImportError:
    SR_AVAILABLE = False


class SpeechRecognitionService:
    """
    Real-time speech recognition service optimized for low latency.
    
    Features:
    - Voice Activity Detection (VAD) using Silero VAD
    - Automatic language detection between Russian and English
    - Streaming audio capture with minimal latency
    - Async processing for non-blocking operations
    """

    def __init__(
        self,
        languages: list[str] | None = None,
        sample_rate: int = 16000,
        chunk_duration_ms: int = 100,
        vad_threshold: float = 0.5,
    ) -> None:
        """
        Initialize speech recognition service.
        
        Args:
            languages: List of languages to recognize (default: ['ru', 'en'])
            sample_rate: Audio sample rate in Hz (default: 16000)
            chunk_duration_ms: Duration of audio chunks in milliseconds
            vad_threshold: Voice activity detection sensitivity threshold
        """
        self._logger = logging.getLogger(self.__class__.__name__)
        
        self.languages = languages or ["ru", "en"]
        self.sample_rate = sample_rate
        self.chunk_duration_ms = chunk_duration_ms
        self.chunk_size = int(sample_rate * chunk_duration_ms / 1000)
        self.vad_threshold = vad_threshold
        
        self._is_active = False
        self._audio_buffer = []
        self._lock = threading.Lock()
        self._recognition_callback: Callable[[str, str], None] | None = None
        self._stream = None
        
        # Initialize Silero VAD model
        self._device = "cuda" if torch.cuda.is_available() else "cpu"
        self._init_vad_model()
        
        # Initialize speech recognition model
        self._init_speech_model()
        
        self._logger.info(
            "SpeechRecognitionService initialized: "
            "languages=%s, sample_rate=%d, device=%s",
            self.languages,
            self.sample_rate,
            self._device
        )

    def _init_vad_model(self) -> None:
        """Initialize Silero VAD model for voice activity detection."""
        try:
            vad_model, _ = torch.hub.load(
                repo_or_dir="snakers4/silero-vad",
                model="silero_vad",
                force_reload=False,
                onnx=True,
            )
            # Note: ONNX models don't support .to() for device movement
            # They are already optimized and don't need to be moved
            self._vad_model = vad_model
            self._logger.info("Silero VAD model loaded successfully")
        except Exception as e:
            self._logger.error("Failed to load Silero VAD model: %s", e)
            self._vad_model = None

    def _init_speech_model(self) -> None:
        """Initialize speech-to-text model."""
        if WHISPER_AVAILABLE:
            try:
                # Using tiny model for low latency. Options: tiny, base, small, medium, large
                # For streaming, tiny is fastest but less accurate
                self._whisper_model = WhisperModel(
                    model_size_or_path="base",
                    device="auto",
                    compute_type="int8",  # Quantized for speed
                )
                self._logger.info("Faster-Whisper model loaded successfully")
            except Exception as e:
                self._logger.error("Failed to load Whisper model: %s", e)
                self._whisper_model = None
        else:
            self._logger.warning("faster-whisper not installed, will use fallback")
            self._whisper_model = None

    def set_recognition_callback(self, callback: Callable[[str, str], None]) -> None:
        """
        Set callback function for recognized text.
        
        Args:
            callback: Function that receives (text: str, language: str)
        """
        self._recognition_callback = callback

    def start(self) -> None:
        """Start listening to microphone input."""
        if self._is_active:
            self._logger.warning("Speech recognition already active")
            return
        
        self._is_active = True
        self._audio_buffer = []
        
        # Start audio stream
        self._stream = sd.InputStream(
            channels=1,
            samplerate=self.sample_rate,
            blocksize=self.chunk_size,
            callback=self._audio_callback,
            latency="low",
        )
        self._stream.start()
        
        self._logger.info("Speech recognition started")

    def stop(self) -> None:
        """Stop listening to microphone input."""
        if not self._is_active:
            return
        
        self._is_active = False
        
        if self._stream:
            self._stream.stop()
            self._stream.close()
            self._stream = None
        
        # Process any remaining audio in buffer
        if self._audio_buffer:
            asyncio.create_task(self._process_audio_buffer())
        
        self._logger.info("Speech recognition stopped")

    def _audio_callback(self, indata, frames, time_info, status) -> None:
        """Callback for audio stream processing."""
        if status:
            self._logger.warning("Audio stream status: %s", status)
        
        # Convert audio data to numpy array
        audio_chunk = indata[:, 0].copy()
        
        with self._lock:
            self._audio_buffer.append(audio_chunk)
            
            # Process when buffer reaches sufficient size
            if len(self._audio_buffer) >= 3:  # ~300ms of audio
                buffer_to_process = np.concatenate(self._audio_buffer)
                self._audio_buffer = []
                
                # Non-blocking processing
                asyncio.create_task(self._process_audio_chunk(buffer_to_process))

    async def _process_audio_chunk(self, audio_data: np.ndarray) -> None:
        """
        Process audio chunk: VAD detection + speech recognition.
        
        Args:
            audio_data: Audio data as numpy array
        """
        try:
            # Check if audio contains voice activity
            if not self._is_voice_active(audio_data):
                return
            
            # Convert audio to torch tensor
            audio_tensor = torch.from_numpy(audio_data.astype(np.float32))
            
            # Recognize speech
            text, language = await self._recognize_speech(audio_tensor)
            
            if text and self._recognition_callback:
                self._recognition_callback(text, language)
                self._logger.info(
                    "Speech recognized: text=%r, language=%s",
                    text,
                    language
                )
        except Exception as e:
            self._logger.error("Error processing audio chunk: %s", e)

    def _is_voice_active(self, audio_data: np.ndarray) -> bool:
        """
        Detect voice activity using Silero VAD.
        
        Args:
            audio_data: Audio data as numpy array
            
        Returns:
            True if voice activity detected, False otherwise
        """
        if self._vad_model is None:
            # Fallback: simple energy-based detection
            return self._energy_based_vad(audio_data)
        
        try:
            audio_tensor = torch.from_numpy(audio_data.astype(np.float32))
            
            # Silero VAD expects sample rate of 16000
            if len(audio_tensor.shape) == 1:
                audio_tensor = audio_tensor.unsqueeze(0)
            
            with torch.no_grad():
                confidence = self._vad_model(audio_tensor, self.sample_rate).item()
            
            return confidence > self.vad_threshold
        except Exception as e:
            self._logger.warning("VAD detection failed: %s, using fallback", e)
            return self._energy_based_vad(audio_data)

    def _energy_based_vad(self, audio_data: np.ndarray) -> bool:
        """
        Simple energy-based voice activity detection as fallback.
        
        Args:
            audio_data: Audio data as numpy array
            
        Returns:
            True if energy threshold exceeded
        """
        energy = np.sqrt(np.mean(audio_data ** 2))
        # Threshold: approximately -40dB
        threshold = 0.01
        return energy > threshold

    async def _process_audio_buffer(self) -> None:
        """Process remaining audio in buffer."""
        with self._lock:
            if not self._audio_buffer:
                return
            
            buffer_to_process = np.concatenate(self._audio_buffer)
            self._audio_buffer = []
        
        await self._process_audio_chunk(buffer_to_process)

    async def _recognize_speech(self, audio_tensor: torch.Tensor) -> tuple[str, str]:
        """
        Recognize speech from audio tensor.
        
        Args:
            audio_tensor: Audio data as torch tensor
            
        Returns:
            Tuple of (recognized_text, detected_language)
        """
        text = ""
        language = "unknown"
        
        if self._whisper_model:
            try:
                # Use faster-whisper for low-latency recognition
                segments, info = self._whisper_model.transcribe(
                    (audio_tensor.cpu().numpy(), self.sample_rate),
                    language="ru" if "ru" in self.languages else "en",
                    beam_size=5,
                    best_of=1,
                    patience=1.0,
                )
                
                text = " ".join(segment.text for segment in segments)
                language = info.language if info else "unknown"
                
                return text, language
            except Exception as e:
                self._logger.error("Whisper recognition failed: %s", e)
        
        # Fallback to speech_recognition library if available
        if SR_AVAILABLE:
            try:
                return await self._recognize_speech_with_sr(audio_tensor)
            except Exception as e:
                self._logger.error("Speech recognition failed: %s", e)
        
        return text, language

    async def _recognize_speech_with_sr(
        self, audio_tensor: torch.Tensor
    ) -> tuple[str, str]:
        """
        Fallback speech recognition using speech_recognition library.
        
        Args:
            audio_tensor: Audio data as torch tensor
            
        Returns:
            Tuple of (recognized_text, detected_language)
        """
        try:
            recognizer = sr.Recognizer()
            
            # Convert tensor to audio_data format
            audio_data = sr.AudioData(
                frame_data=audio_tensor.cpu().numpy().tobytes(),
                sample_rate=self.sample_rate,
                sample_width=2,
            )
            
            # Try to recognize in Russian first, then English
            text = ""
            language = "ru"
            
            try:
                # Try Russian recognition
                text = recognizer.recognize_google(audio_data, language="ru-RU")
            except sr.HTTPError:
                try:
                    # Try English recognition
                    text = recognizer.recognize_google(audio_data, language="en-US")
                    language = "en"
                except sr.HTTPError as e:
                    self._logger.warning("Google Speech Recognition API error: %s", e)
            except sr.UnknownValueError:
                self._logger.info("Could not understand audio")
            
            return text, language
        except Exception as e:
            self._logger.error("Fallback speech recognition failed: %s", e)
            return "", "unknown"

    def get_status(self) -> dict[str, bool | str | list[str]]:
        """
        Get current service status.
        
        Returns:
            Dictionary with service status information
        """
        return {
            "is_active": self._is_active,
            "languages": self.languages,
            "sample_rate": self.sample_rate,
            "vad_model_available": self._vad_model is not None,
            "whisper_model_available": self._whisper_model is not None,
            "device": self._device,
            "buffer_size": len(self._audio_buffer),
        }
