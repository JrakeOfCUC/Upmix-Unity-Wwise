import unittest

from song_catalog import SONGS, UNITY_SOURCE, unity_source


class SongCatalogContract(unittest.TestCase):
    def test_unity_projection_is_current(self):
        self.assertEqual(UNITY_SOURCE.read_text(encoding="utf-8"), unity_source())

    def test_existing_layout_keys_and_scene_ordinals(self):
        keys = ("WoBuNanGuo", "SeeYouAgain", "GloriousYears", "SmokeOnTheWater",
                "FiveHundredMiles", "ShuoSanJiuSan", "FurElise", "XiangShanSheGu")
        for ordinal, key in enumerate(keys):
            self.assertEqual((SONGS[ordinal].ordinal, SONGS[ordinal].key), (ordinal, key))
