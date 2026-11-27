"""Re-render the public fan voice pack. Ordinary app builds use the committed MP3s."""

import argparse
import hashlib
import json
from pathlib import Path
import subprocess
from tempfile import TemporaryDirectory

import numpy as np
import onnxruntime as ort
from kokoro_onnx import Kokoro


ASSETS = Path(__file__).resolve().parents[1] / "assets"
STYLES = {
    "Neuro": ("af_bella", "en-us", 1.06),
    "Evil": ("af_heart", "en-us", 0.98),
    "Vedal": ("bm_george", "en-gb", 0.97),
}
MODEL_HASH = "6e742170d309016e5891a994e1ce1559c702a2ccd0075e67ef7157974f6406cb"
STYLES_HASH = "bca610b8308e8d99f32e6fe4197e7ec01679264efed0cac9140fe9c29f1fbf7d"


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def render(engine, cue, target):
    voice, language, speed = STYLES[cue["Speaker"]]
    words = cue["Line"].replace("’", "'").replace("Tutel", "Tootle").replace("Vedal", "Vee-dal")
    samples, rate = engine.create(words, voice=voice, lang=language, speed=speed, trim=False)
    active = np.flatnonzero(np.abs(samples) > 0.003)
    if not active.size or not np.all(np.isfinite(samples)):
        raise ValueError(f"Empty or invalid render for {cue['Id']}")
    samples = samples[max(0, active[0] - rate // 24):active[-1] + rate // 12]
    if not 0.2 <= len(samples) / rate <= 10:
        raise ValueError(f"Unexpected duration for {cue['Id']}")
    rms = np.sqrt(np.mean(samples ** 2))
    samples *= min(0.095 / rms, 0.72 / np.max(np.abs(samples)))
    subprocess.run([
        "ffmpeg", "-nostdin", "-v", "error", "-y", "-f", "f32le", "-ar", str(rate),
        "-ac", "1", "-i", "pipe:0", "-map_metadata", "-1", "-c:a", "libmp3lame",
        "-b:a", "96k", "-id3v2_version", "0", "-write_id3v1", "0", str(target),
    ], input=samples.astype("<f4").tobytes(), check=True)
    cue.update(Seconds=round(len(samples) / rate, 2), Gain=1,
               Source="https://huggingface.co/hexgrad/Kokoro-82M",
               Credit=f"同人合成配音 · Kokoro {voice}", Sha256=digest(target))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("model", type=Path)
    parser.add_argument("styles", type=Path)
    args = parser.parse_args()
    if digest(args.model) != MODEL_HASH or digest(args.styles) != STYLES_HASH:
        raise ValueError("Use the pinned Kokoro v1.0 int8 model and style archive.")
    options = ort.SessionOptions()
    options.intra_op_num_threads = 4
    session = ort.InferenceSession(str(args.model), options, providers=["CPUExecutionProvider"])
    engine = Kokoro.from_session(session, str(args.styles))
    manifest = ASSETS / "voices.json"
    cues = json.loads(manifest.read_text(encoding="utf-8"))
    output = ASSETS / "voices"
    output.mkdir(exist_ok=True)
    with TemporaryDirectory(dir=output) as tmp:
        stage = Path(tmp)
        for cue in cues:
            render(engine, cue, stage / (cue["Id"] + ".mp3"))
        for clip in stage.glob("*.mp3"):
            clip.replace(output / clip.name)
        catalog = stage / "voices.json"
        catalog.write_text(json.dumps(cues, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
        catalog.replace(manifest)


if __name__ == "__main__":
    main()
