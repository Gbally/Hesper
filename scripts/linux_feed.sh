#!/usr/bin/env bash
# After the "Linux app" workflow published an AppImage to a release (Actions → Linux app → Run workflow,
# with a tag): puts that release's update feed in the repository, for installed apps to find it.
#
#   scripts/linux_feed.sh v0.7.3          → latest-linux.json
#   scripts/linux_feed.sh v0.7.3 --beta   → latest-linux-beta.json (a test version)
#
# Then commit and push the file. The site's Linux button and the apps read it from main.
set -euo pipefail
cd "$(dirname "$0")/.."
TAG=${1:?usage: scripts/linux_feed.sh vX.Y.Z [--beta]}
FEED=latest-linux.json
[ "${2:-}" = "--beta" ] && FEED=latest-linux-beta.json
curl -fsSL -o "$FEED.tmp" "https://github.com/Gbally/Hesper/releases/download/$TAG/$FEED" \
  || { rm -f "$FEED.tmp"; echo "Release $TAG has no $FEED: run the Linux app workflow with that tag first."; exit 1; }
python3 -c "import json,sys; d=json.load(open(sys.argv[1])); assert d['url'].startswith('https://github.com/Gbally/Hesper/') and len(d['sha256']) == 64" "$FEED.tmp"
mv "$FEED.tmp" "$FEED"
cat "$FEED"
echo
echo "OK: $FEED — commit and push it."
