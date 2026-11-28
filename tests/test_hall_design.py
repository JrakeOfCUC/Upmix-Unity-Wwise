import json
from pathlib import Path
import runpy
import unittest


class HallDesignContract(unittest.TestCase):
    def test_manifest_and_unity_match_design(self):
        design = Path(__file__).resolve().parents[1] / "docs/spatial-layout"
        model = runpy.run_path(str(design / "hall_design.py"))
        self.assertEqual(json.loads((design / "layout-design.json").read_text(encoding="utf-8")),
                         model["manifest"])
        self.assertEqual(model["UNITY_SOURCE"].read_text(encoding="utf-8"), model["unity_source"]())
        returns = [obj["id"] for obj in model["objects"] if not obj["event"]]
        self.assertEqual(returns, ["E1", "E2", "R1", "R2", "R3", "R4", "R5", "R6"])
