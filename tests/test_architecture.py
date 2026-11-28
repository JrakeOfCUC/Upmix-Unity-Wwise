"""Executable boundaries for first-party sources. Vendor integration follows its own ABI."""
import ast
import graphlib
from pathlib import Path
import re
import subprocess
import unittest


ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "WwiseUpmix" / "Assets"


class ArchitectureContract(unittest.TestCase):
    def test_build_and_design_imports_are_acyclic(self):
        tracked = subprocess.check_output(["git", "ls-files", "-z", "*.py"], cwd=ROOT)
        files = [ROOT / name.decode() for name in tracked.split(b"\0") if name]
        files = [path for path in files if "tests" not in path.relative_to(ROOT).parts]
        # Build tools are standalone Python modules. Resolve each import from the
        # importing script's directory and repository root, as in the CLI entry points.
        modules = set(files)
        graph = {}
        for path in files:
            imports = set()
            for node in ast.walk(ast.parse(path.read_text(encoding="utf-8-sig"))):
                if isinstance(node, ast.Import):
                    imports.update(alias.name for alias in node.names)
                elif isinstance(node, ast.ImportFrom) and node.module:
                    imports.add(node.module)
            candidates = {base / (name.replace(".", "/") + ".py")
                          for base in (path.parent, ROOT) for name in imports}
            graph[path.relative_to(ROOT)] = {dep.relative_to(ROOT) for dep in candidates & modules}
        tuple(graphlib.TopologicalSorter(graph).static_order())

    def test_wwise_bank_ownership_and_mixer_calls(self):
        bank_call = re.compile(r"\bAkUnitySoundEngine\s*\.\s*(?:LoadBank|UnloadBank)\s*\(")
        for path in ASSETS.rglob("*.cs"):
            if {"Wwise", "Editor"} & set(path.relative_to(ASSETS).parts):
                continue
            if path != ASSETS / "LoadedSoundBank.cs":
                self.assertNotRegex(path.read_text(encoding="utf-8-sig"), bank_call, str(path))
        mixer = (ASSETS / "SpatialStemPlayer.Mixer.cs").read_text(encoding="utf-8-sig")
        self.assertNotRegex(mixer, r"\b(?:AkUnitySoundEngine|File)\s*\.")
