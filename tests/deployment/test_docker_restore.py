"""Exercise Docker's project-files-only restore without requiring Docker."""

import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]


class DockerRestoreTests(unittest.TestCase):
    def test_project_only_restore_includes_blazor_bootstrap(self):
        scratch = ROOT / "work-tests"
        scratch.mkdir(exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="docker-restore-", dir=scratch) as folder:
            stage = Path(folder)
            shutil.copy2(ROOT / "global.json", stage)
            for name in ("VideoNote.Server", "VideoNote.Client", "VideoNote.Shared"):
                (stage / name).mkdir()
                shutil.copy2(ROOT / name / f"{name}.csproj", stage / name)
            restored = subprocess.run(
                ["dotnet", "restore", "VideoNote.Server/VideoNote.Server.csproj"],
                cwd=stage, capture_output=True, text=True, encoding="utf-8", errors="replace",
                timeout=180,
            )
            self.assertEqual(restored.returncode, 0, restored.stdout + restored.stderr)
            assets = json.loads((stage / "VideoNote.Server/obj/project.assets.json").read_text())
            self.assertTrue(
                any("_framework/blazor.web.js" in package.get("files", [])
                    for package in assets["libraries"].values()),
                "Project-only restore omitted blazor.web.js; Docker publish --no-restore will produce a blank page.",
            )


if __name__ == "__main__":
    unittest.main()
