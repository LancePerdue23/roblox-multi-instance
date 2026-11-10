"""Use live public badges or private snapshots. Never embed credentials."""
import html
import json
import os
from pathlib import Path
import re
from urllib.request import Request, urlopen

ROOT = Path(__file__).resolve().parents[1]


def refresh(root, repository, fetch):
    readme = root / 'README.md'
    text = readme.read_text(encoding='utf-8')
    if not fetch(f'repos/{repository}')['private']:
        images = {
            'Release': f'https://img.shields.io/github/v/release/{repository}?label=release&sort=semver&filter=v%2A',
            'Downloads': f'https://img.shields.io/github/downloads/{repository}/total',
        }
    else:
        releases, counts, page = [], {}, 1
        while True:
            batch = fetch(f'repos/{repository}/releases?per_page=100&page={page}')
            for release in batch:
                if release['draft']:
                    continue
                releases.append(release)
                for asset in release['assets']:
                    counts[asset['id']] = max(counts.get(asset['id'], 0), asset['download_count'])
            if len(batch) < 100:
                break
            page += 1
        stable = [r for r in releases if not r['prerelease'] and re.fullmatch(r'v\d+\.\d+\.\d+', r['tag_name'])]
        latest = max(stable, key=lambda r: tuple(map(int, r['tag_name'][1:].split('.'))), default=None)
        label = latest['tag_name'] if latest else 'no stable release'
        for name, title, value in (('release', 'release', label), ('downloads', 'downloads', str(sum(counts.values())))):
            left, right = len(title) * 7 + 12, len(value) * 7 + 12
            accessible = html.escape(f'{title}: {value}', quote=True)
            svg = f'<svg xmlns="http://www.w3.org/2000/svg" width="{left + right}" height="20" role="img" aria-label="{accessible}"><title>{accessible}</title><rect width="{left + right}" height="20" rx="3" fill="#555"/><path fill="#007ec6" d="M{left} 0h{right}v20H{left}z"/><g fill="#fff" text-anchor="middle" font-family="Verdana,DejaVu Sans,sans-serif" font-size="11"><text x="{left / 2:g}" y="14">{title}</text><text x="{left + right / 2:g}" y="14">{html.escape(value)}</text></g></svg>\n'
            (root / f'docs/assets/badges/{name}.svg').write_text(svg, encoding='utf-8')
        images = {name: f'docs/assets/badges/{name.lower()}.svg' for name in ('Release', 'Downloads')}
    for name, url in images.items():
        text, changed = re.subn(rf'\[!\[{name}\]\([^\n]+?\)\]\([^\n]+?\)', f'[![{name}]({url})](https://github.com/{repository}/releases)', text)
        if changed != 1:
            raise ValueError(f'Expected one {name} badge; found {changed}')
    readme.write_text(text, encoding='utf-8')


def main():
    repository = os.environ.get('GITHUB_REPOSITORY', 'LancePerdue23/roblox-multi-instance')
    token = os.environ['GH_TOKEN']
    def fetch(route):
        request = Request(f'https://api.github.com/{route}', headers={
            'Authorization': f'Bearer {token}', 'Accept': 'application/vnd.github+json',
            'User-Agent': 'rmi-release-badges', 'X-GitHub-Api-Version': '2022-11-28',
        })
        with urlopen(request, timeout=30) as response:
            return json.load(response)
    refresh(ROOT, repository, fetch)


if __name__ == '__main__':
    main()
