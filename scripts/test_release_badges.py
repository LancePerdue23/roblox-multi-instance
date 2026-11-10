import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import xml.etree.ElementTree as ET

spec = importlib.util.spec_from_file_location('badges', Path(__file__).with_name('Update-DownloadBadge.py'))
badges = importlib.util.module_from_spec(spec)
spec.loader.exec_module(badges)


class ReleaseBadges(unittest.TestCase):
    def test_pagination_allowlist_prereleases_drafts_and_deduplication(self):
        releases = [dict(id=1, draft=False, prerelease=False),
                    dict(id=2, draft=False, prerelease=True),
                    dict(id=3, draft=True)]
        releases += [dict(id=i, draft=True) for i in range(4, 101)]
        def asset(i, name, count):
            return dict(id=i, name=name, download_count=count)
        routes = {
            'repos/owner/repo/releases?per_page=100&page=1': releases,
            'repos/owner/repo/releases?per_page=100&page=2': [],
            'repos/owner/repo/releases/1/assets?per_page=100&page=1': [
                asset(1, 'RobloxMultiInstance-Setup.exe', 10),
                asset(2, 'RobloxMultiInstance-Portable.zip', 4),
                asset(3, 'SHA256SUMS.txt', 900),
                asset(4, 'assets.json', 900),
            ] + [asset(i, 'ignored.txt', 900) for i in range(5, 101)],
            'repos/owner/repo/releases/1/assets?per_page=100&page=2': [
                asset(5, 'RobloxLaunchHelper-Setup.exe', 3)],
            'repos/owner/repo/releases/2/assets?per_page=100&page=1': [
                asset(1, 'RobloxMultiInstance-Setup.exe', 11),
                asset(6, 'RobloxLaunchHelper-Setup.exe', 2)],
        }
        calls = []
        def fetch(route):
            calls.append(route)
            return routes[route]
        self.assertEqual(badges.download_total('owner/repo', fetch), 20)
        self.assertCountEqual(calls, routes)

    def test_empty_repository_and_artifact_output(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            readme = root / 'README.md'
            readme.write_text('Keep this unchanged.')
            output = root / 'public'
            badges.publish(output, 'owner/repo', lambda route: [])
            self.assertEqual(readme.read_text(), 'Keep this unchanged.')
            svg = ET.parse(output / 'downloads.svg').getroot()
            self.assertEqual(svg.attrib['aria-label'], 'downloads: 0')
            self.assertEqual(json.loads((output / 'downloads.json').read_text())['message'], '0')
            self.assertTrue((output / 'index.html').exists())

    def test_invalid_count_fails_instead_of_publishing_a_wrong_total(self):
        for value in (-1, '10', None, True):
            with self.subTest(value=value):
                def fetch(route):
                    if '/assets?' in route:
                        return [dict(id=1, name='RobloxMultiInstance-Setup.exe', download_count=value)]
                    return [dict(id=1, draft=False)]
                with self.assertRaises(ValueError):
                    badges.download_total('owner/repo', fetch)


if __name__ == '__main__':
    unittest.main()
