"""Batch narration with Piper: reads [{"text", "output_file"}] JSON on stdin, writes one WAV per item."""
import json
import sys
import wave

from piper import PiperVoice

voice = PiperVoice.load(sys.argv[1])
for job in json.load(sys.stdin):
    with wave.open(job["output_file"], "wb") as w:
        voice.synthesize_wav(job["text"], w)
