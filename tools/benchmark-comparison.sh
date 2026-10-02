#!/usr/bin/env bash
set -euo pipefail
# Use the same fully optimized JIT tier in both processes; avoid tier-promotion timing bias.
export DOTNET_TieredCompilation=0
baseline=b8d6988dd5d5b320dd5782aa52a6b1169c13da30
root="$PWD"
tmp="${RUNNER_TEMP:-/tmp}/presentationspace-benchmark-$$"
mkdir -p "$root/artifacts/performance"
rm -f "$root/artifacts/performance/import-source.pptx"
git worktree add --detach "$tmp" "$baseline"
trap 'git worktree remove --force "$tmp"' EXIT
mkdir -p "$tmp/tests/PresentationSpace.Performance"
cp tests/PresentationSpace.Performance/*.cs tests/PresentationSpace.Performance/*.csproj "$tmp/tests/PresentationSpace.Performance/"
(cd "$tmp" && dotnet run --project tests/PresentationSpace.Performance -c Release -- "$root/artifacts/performance/baseline.json")
dotnet run --project tests/PresentationSpace.Performance -c Release -- "$root/artifacts/performance/current.json"
python3 - <<'PY'
import json
from pathlib import Path
root=Path('artifacts/performance')
before=json.loads((root/'baseline.json').read_text()); after=json.loads((root/'current.json').read_text())
assert before['importSourceSha256'] == after['importSourceSha256'], 'Import workloads must use byte-identical packages'
lines=['# Same-runner CPU/raster benchmark', '', 'Baseline: `b8d6988` (0.11). Same driver and .NET runtime; tiered compilation disabled in both processes; warm-up excluded. These are synthetic CPU/raster measurements, not browser FPS or physical-GPU timings.', '', '| Workload | Baseline median ms | Current median ms | Speed ratio | Baseline bytes/op | Current bytes/op |', '|---|---:|---:|---:|---:|---:|']
for a,b in zip(before['results'],after['results']):
 assert a['name']==b['name']
 ratio=a['medianMilliseconds']/max(.000001,b['medianMilliseconds'])
 lines.append(f"| {a['name']} | {a['medianMilliseconds']:.4f} | {b['medianMilliseconds']:.4f} | {ratio:.2f}× | {a['bytesPerOperation']:.0f} | {b['bytesPerOperation']:.0f} |")
lines.extend(['', f"Import fixture SHA-256: {before['importSourceSha256']}.", '', f"Repeated-picture PPTX bytes: {before['repeatedPicturePptxBytes']:,} → {after['repeatedPicturePptxBytes']:,}."])
(root/'comparison.md').write_text('\n'.join(lines)+'\n')
print('\n'.join(lines))
PY
