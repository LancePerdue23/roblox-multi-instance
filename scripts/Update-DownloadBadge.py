"""Refresh the README badge from published GitHub package asset counts."""
import json
import os
from pathlib import Path
import re
from urllib.request import Request, urlopen

repository = os.environ.get("GITHUB_REPOSITORY", "LancePerdue23/roblox-multi-instance")
token = os.environ["GH_TOKEN"]
packages = {"RobloxMultiInstance-Setup.exe", "RobloxLaunchHelper-Setup.exe", "RobloxMultiInstance-Portable.zip"}
counts = {}
page = 1
while True:
    request = Request(
        f"https://api.github.com/repos/{repository}/releases?per_page=100&page={page}",
        headers={"Authorization": f"Bearer {token}", "Accept": "application/vnd.github+json", "User-Agent": "rmi-download-badge"},
    )
    with urlopen(request, timeout=30) as response:
        releases = json.load(response)
    for release in releases:
        if release["draft"]:
            continue
        for asset in release["assets"]:
            if asset["name"] in packages:
                counts[asset["id"]] = max(counts.get(asset["id"], 0), asset["download_count"])
    if len(releases) < 100:
        break
    page += 1
count = str(sum(counts.values()))
path = Path(__file__).resolve().parents[1] / "docs/assets/badges/downloads.svg"
svg = path.read_text(encoding="utf-8")
svg = re.sub(r"package downloads: \d+", f"package downloads: {count}", svg)
width = max(22, len(count) * 8 + 10)
svg = re.sub(r'(<text x="[\d.]+" y="14">)\d+(</text>)', lambda m: f'<text x="{118 + width / 2:g}" y="14">{count}</text>', svg)
svg = re.sub(r'width="\d+"', f'width="{118 + width}"', svg)
svg = re.sub(r'M118 0h\d+v20H118z', f'M118 0h{width}v20H118z', svg)
path.write_text(svg, encoding="utf-8")
print(f"Package downloads: {count}")

