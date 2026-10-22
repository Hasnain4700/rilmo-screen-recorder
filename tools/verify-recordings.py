"""Fully decode selected, owned test recordings; retain hashes and decoder logs."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import imageio_ffmpeg

parser = argparse.ArgumentParser()
parser.add_argument('--directory', action='append', required=True)
parser.add_argument('--report', required=True)
args = parser.parse_args()
artifacts = Path(__file__).resolve().parents[1] / 'artifacts'
report = Path(args.report).resolve()
if not report.is_relative_to(artifacts):
    raise ValueError('Report must remain in workspace/artifacts.')
files = set()
for value in args.directory:
    directory = Path(value).resolve()
    if not directory.is_relative_to(artifacts) or not directory.is_dir():
        raise ValueError('Select an owned test output directory in workspace/artifacts.')
    files.update(p for p in directory.rglob('*') if p.suffix.lower() in ('.mkv', '.mp4'))
if not files:
    raise ValueError('No recordings selected.')
ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
results = []
for file in sorted(files):
    process = subprocess.run([ffmpeg, '-hide_banner', '-xerror', '-threads', '2',
                              '-i', str(file), '-map', '0', '-f', 'null', 'NUL'],
                             capture_output=True, text=True, timeout=120)
    file.with_suffix(file.suffix + '.decode.txt').write_text(process.stderr, encoding='utf-8')
    frames = re.findall(r'frame=\s*(\d+)', process.stderr)
    item = dict(file=str(file), passed=process.returncode == 0 and bool(frames) and int(frames[-1]) > 0,
                decodedFrames=int(frames[-1]) if frames else 0,
                audioTracks=len(re.findall(r'Stream #0:\d+.*Audio:', process.stderr.split('Stream mapping:')[0])),
                sha256=hashlib.file_digest(file.open('rb'), 'sha256').hexdigest())
    results.append(item)
    print(f"{file.name}: {'PASS' if item['passed'] else 'FAIL'}, {item['decodedFrames']} frames", flush=True)
report.write_text(json.dumps(dict(passed=all(x['passed'] for x in results), decoder=ffmpeg,
                                 decoderSha256=hashlib.file_digest(Path(ffmpeg).open('rb'), 'sha256').hexdigest(),
                                 files=results), indent=2), encoding='utf-8')
if not all(x['passed'] for x in results):
    raise SystemExit(1)
