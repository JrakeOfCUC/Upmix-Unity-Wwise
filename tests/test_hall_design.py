import json
from pathlib import Path
import hall_design
import unittest


class HallDesignContract(unittest.TestCase):
    def test_manifest_and_unity_match_design(self):
        design = Path(__file__).resolve().parents[1] / "docs/spatial-layout"
        self.assertEqual(json.loads((design / "layout-design.json").read_text(encoding="utf-8")),
                         hall_design.manifest)
        self.assertEqual(hall_design.UNITY_SOURCE.read_text(encoding="utf-8"), hall_design.unity_source())
        returns = [obj["id"] for obj in hall_design.objects if not obj["event"]]
        self.assertEqual(returns, ["E1", "E2", "R1", "R2", "R3", "R4", "R5", "R6"])
