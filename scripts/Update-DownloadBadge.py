"""Publish runnable-package download counts as Pages artifacts, never commits."""
import html
import json
import os
from pathlib import Path
from urllib.request import Request, urlopen

ROOT = Path(__file__).resolve().parents[1]
BINARY_NAMES = frozenset((
    'RobloxMultiInstance-Setup.exe',
    'RobloxLaunchHelper-Setup.exe',
    'RobloxMultiInstance-Portable.zip',
))


def paginate(fetch, route):
    page = 1
    while True:
        batch = fetch(f'{route}?per_page=100&page={page}')
        yield from batch
        if len(batch) < 100:
            break
        page += 1


def download_total(repository, fetch):
    counts = {}
    for release in paginate(fetch, f'repos/{repository}/releases'):
        if release['draft']:
            continue
        for asset in paginate(fetch, f"repos/{repository}/releases/{release['id']}/assets"):
            if asset['name'] not in BINARY_NAMES:
                continue
            count = asset['download_count']
            if type(count) is not int or count < 0:
                raise ValueError('Invalid GitHub download count')
            counts[asset['id']] = max(counts.get(asset['id'], 0), count)
    return sum(counts.values())


def publish(output, repository, fetch):
    total = download_total(repository, fetch)
    title, value = 'downloads', str(total)
    left, right = len(title) * 7 + 12, len(value) * 7 + 12
    accessible = html.escape(f'{title}: {value}', quote=True)
    svg = f'<svg xmlns="http://www.w3.org/2000/svg" width="{left + right}" height="20" role="img" aria-label="{accessible}"><title>{accessible}</title><rect width="{left + right}" height="20" rx="3" fill="#555"/><path fill="#007ec6" d="M{left} 0h{right}v20H{left}z"/><g fill="#fff" text-anchor="middle" font-family="Verdana,DejaVu Sans,sans-serif" font-size="11"><text x="{left / 2:g}" y="14">{title}</text><text x="{left + right / 2:g}" y="14">{value}</text></g></svg>\n'
    output.mkdir(parents=True, exist_ok=True)
    (output / 'downloads.svg').write_text(svg, encoding='utf-8')
    (output / 'downloads.json').write_text(json.dumps({'schemaVersion': 1, 'label': title, 'message': value, 'color': 'blue'}) + '\n', encoding='utf-8')
    (output / 'index.html').write_text('<!doctype html><title>Download count</title><img src="downloads.svg" alt="Download count">\n', encoding='utf-8')


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

    publish(ROOT / '.build/download-badge', repository, fetch)


if __name__ == '__main__':
    main()
