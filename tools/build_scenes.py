"""Build two reproducible, data-only ZIPs using the Dream Skin v1 package format."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
import zipfile


HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
SCENES = ROOT / "scenes"
CLIENT_VERSION = "1.5.21"
RELEASE_DATE = (2026, 11, 27, 0, 0, 0)
MEDIA = {
    "theme.json": "application/json",
    "theme.css": "text/css",
    "background.jpg": "image/jpeg",
    "LICENSE.txt": "text/plain",
}


def encode_json(data):
    return (json.dumps(data, ensure_ascii=False, indent=2) + "\n").encode("utf-8")


def read_text_bytes(path):
    return path.read_text(encoding="utf-8-sig").encode("utf-8")


def load_background(path):
    with path.open("rb") as stream:
        data = stream.read(10 * 1024 * 1024 + 1)
    if not 20 <= len(data) <= 10 * 1024 * 1024:
        raise ValueError(f"{path.name} exceeds Dream Skin's image limits or is truncated")
    if data[:2] != b"\xff\xd8" or data[-2:] != b"\xff\xd9":
        raise ValueError(f"{path.name} must be a complete JPEG")
    return data


def package_files(slot):
    theme = json.loads((SCENES / f"{slot}.json").read_text(encoding="utf-8"))
    files = {
        "theme.json": encode_json(theme),
        "theme.css": read_text_bytes(SCENES / "theme.css"),
        "background.jpg": load_background(ROOT / "assets" / f"{slot}.jpg"),
        "LICENSE.txt": read_text_bytes(ROOT / "assets" / "NOTICE.md") +
                       b"\n\nTheme definition and CSS\n\n" + read_text_bytes(ROOT / "LICENSE") +
                       b"\n\nDream Skin\n\n" + read_text_bytes(ROOT / "assets" / "DREAM-SKIN-LICENSE.txt"),
    }
    manifest = {
        "packageVersion": 1,
        "themeId": theme["id"],
        "version": "1.1.3",
        "skinApiVersion": 1,
        "minClientVersion": CLIENT_VERSION,
        "platforms": ["macos", "windows"],
        "capabilities": ["background", "tokens", "safe-css"],
        "publisher": {"id": "twin-shift", "displayName": "Twin Shift contributors"},
        "license": "LicenseRef-TwinShift-Fan-Art",
        "provenance": {
            "aiGenerated": True,
            "summary": "Unofficial fan scenery created with ImageGen. See LICENSE.txt for asset terms.",
        },
        "files": [
            {"path": name, "mediaType": MEDIA[name], "bytes": len(data),
             "sha256": hashlib.sha256(data).hexdigest()}
            for name, data in sorted(files.items())
        ],
        "createdAt": "2026-11-27T00:00:00Z",
    }
    return {**files, "manifest.json": encode_json(manifest)}


def build(slot, scratch, validator):
    files = package_files(slot)
    character = {"neo": "neuro", "evo": "Evil"}[slot]
    archive = scratch / f"TwinShift-{character}.zip"
    # Fixed metadata and stored entries make the same inputs reproducible across platforms.
    with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_STORED) as out:
        for name, data in sorted(files.items()):
            info = zipfile.ZipInfo(name, RELEASE_DATE)
            info.create_system = 3
            info.external_attr = 0o100644 << 16
            out.writestr(info, data)
    if validator:
        source = scratch / slot
        source.mkdir()
        with zipfile.ZipFile(archive) as result:
            for name in files:
                (source / name).write_bytes(result.read(name))
        for platform in ("windows", "macos"):
            stage = scratch / f"{slot}-{platform}"
            stage.mkdir()
            subprocess.run(
                ["node", str(validator), "--source", str(source), "--stage", str(stage),
                 "--platform", platform, "--client-version", CLIENT_VERSION],
                check=True,
            )
    return archive


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", type=Path, default=ROOT / "out" / "scenes")
    parser.add_argument("--validator", type=Path, help="Local v1.5.21 runtime/theme-package-validator.mjs")
    args = parser.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=args.out) as temporary_directory:
        archives = [build(slot, Path(temporary_directory), args.validator) for slot in ("neo", "evo")]
        for archive in archives:
            destination = args.out / archive.name
            os.replace(archive, destination)
            print(destination)


if __name__ == "__main__":
    main()
