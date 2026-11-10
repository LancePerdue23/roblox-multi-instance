import importlib.util
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('badges', Path(__file__).with_name('Update-DownloadBadge.py'))
badges = importlib.util.module_from_spec(spec)
spec.loader.exec_module(badges)


class ReleaseBadges(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / 'docs/assets/badges').mkdir(parents=True)
        self.readme = self.root / 'README.md'
        self.readme.write_text('[![Release](docs/assets/badges/release.svg)](https://example.invalid/old)\n[![Downloads](docs/assets/badges/downloads.svg)](https://example.invalid/old)\nKeep this paragraph.\n')

    def test_public_switch_and_repeat_without_release_requests(self):
        def fetch(route):
            self.assertEqual(route, 'repos/owner/repo')
            return {'private': False}
        snapshot = self.root / 'docs/assets/badges/downloads.svg'
        snapshot.write_text('unchanged')
        badges.refresh(self.root, 'owner/repo', fetch)
        first = self.readme.read_text()
        badges.refresh(self.root, 'owner/repo', fetch)
        self.assertEqual(first, self.readme.read_text())
        self.assertIn('https://img.shields.io/github/v/release/owner/repo?label=release', first)
        self.assertIn('https://img.shields.io/github/downloads/owner/repo/total', first)
        self.assertIn('Keep this paragraph.', first)
        self.assertEqual(snapshot.read_text(), 'unchanged')

    def test_private_pagination_all_assets_drafts_dedup_and_stable_order(self):
        def release(tag, assets, prerelease=False, draft=False):
            return dict(tag_name=tag, assets=[dict(id=i, download_count=n, name=name) for i,n,name in assets], prerelease=prerelease, draft=draft)
        batch = [release('v1.9.0', [(1,2,'setup.exe'),(2,5,'SHA256SUMS.txt')]),
                 release('v1.10.0', [(3,4,'assets.json')]),
                 release('v9.0.0', [(4,6,'portable.zip')], prerelease=True),
                 release('helper-v8.0.0', []),
                 release('v10.0.0', [(5,999,'hidden.exe')], draft=True)]
        batch += [release('v1.0.0', [])] * (100-len(batch))
        calls=[]
        def fetch(route):
            calls.append(route)
            if '?' not in route:return {'private': True}
            return batch if route.endswith('page=1') else [release('v1.9.0', [(1,3,'setup.exe')])]
        badges.refresh(self.root, 'owner/repo', fetch)
        self.assertEqual(len(calls),3)
        self.assertIn('downloads: 18', (self.root/'docs/assets/badges/downloads.svg').read_text())
        self.assertIn('release: v1.10.0', (self.root/'docs/assets/badges/release.svg').read_text())
        self.assertNotIn('img.shields.io', self.readme.read_text())

    def test_no_stable_release_is_explicit(self):
        badges.refresh(self.root, 'owner/repo', lambda route: [] if '?' in route else {'private':True})
        self.assertIn('no stable release', (self.root/'docs/assets/badges/release.svg').read_text())
        self.assertIn('downloads: 0', (self.root/'docs/assets/badges/downloads.svg').read_text())


if __name__ == '__main__':
    unittest.main()
