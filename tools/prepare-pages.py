"""Locate Uno's publish web root without depending on a generated hash directory."""
import json
import os
import shutil
import sys
from pathlib import Path

source, destination = map(Path, sys.argv[1:3])
candidates = sorted(source.rglob('index.html'), key=lambda p: len(p.parts))
if not candidates:
    raise SystemExit('Uno publish produced no index.html; refusing to deploy an empty site.')
webroot = candidates[0].parent
if not list(webroot.rglob('*.wasm')):
    raise SystemExit(f'No WebAssembly binaries found in {webroot}; refusing to deploy.')
shutil.copytree(webroot, destination, dirs_exist_ok=True)
(destination / '.nojekyll').touch()
shutil.copyfile(destination / 'index.html', destination / '404.html')
(destination / 'build-info.json').write_text(json.dumps({'commit': os.getenv('GITHUB_SHA', 'local'), 'unoSdk': '6.7.30', 'application': 'PresentationSpace'}, indent=2))
print(f'Prepared {len(list(destination.rglob("*")))} files from {webroot}')
