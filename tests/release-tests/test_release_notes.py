"""Exercise the publishing gate through the same CLI used by GitHub Actions."""

import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]


class ReleaseNotesTests(unittest.TestCase):
    def run_script(self, tag, changelog=None):
        with tempfile.TemporaryDirectory() as directory:
            temp = Path(directory)
            source = ROOT / "changelog.md"
            if changelog is not None:
                source = temp / "changelog.md"
                source.write_bytes(changelog.encode("utf-8-sig"))
            output = temp / "notes.md"
            github_output = temp / "github-output"
            result = subprocess.run(
                [sys.executable, str(ROOT / "scripts/release-notes.py"), tag,
                 "--changelog", str(source), "--output", str(output)],
                env={**os.environ, "GITHUB_OUTPUT": str(github_output)},
                capture_output=True, text=True, encoding="utf-8",
            )
            return (result, output.read_text(encoding="utf-8") if output.exists() else None,
                    github_output.read_text(encoding="utf-8") if github_output.exists() else None)

    def test_repository_release_and_case_insensitive_tag(self):
        expected = (ROOT / "changelog.md").read_text(encoding="utf-8").split("## V0.1.0", 1)[1]
        for tag in ("V0.1.0", "v0.1.0"):
            with self.subTest(tag=tag):
                result, notes, outputs = self.run_script(tag)
                self.assertEqual(result.returncode, 0, result.stderr)
                self.assertEqual(notes, "## V0.1.0" + expected.rstrip() + "\n")
                self.assertEqual(outputs, "version=0.1.0\n")

    def test_missing_version_fails_before_writing_outputs(self):
        result, notes, outputs = self.run_script("V9.9.9")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Missing changelog section for version 9.9.9", result.stderr)
        self.assertIsNone(notes)
        self.assertIsNone(outputs)

    def test_section_boundaries_bom_crlf_and_lowercase_heading(self):
        result, notes, outputs = self.run_script(
            "V12.34.56", "# Log\r\n## V13.0.0\r\nnew\r\n## v12.34.56\r\n\r\n"
            "### 新功能\r\n- 内容\r\n\r\n## V1.0.0\r\nold\r\n")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(notes, "## v12.34.56\n\n### 新功能\n- 内容\n")
        self.assertEqual(outputs, "version=12.34.56\n")

    def test_invalid_tags_fail(self):
        for tag in ("release", "0.1.0", "V0.1", "V0.1.0-rc.1", "V0x1x0", "V0.1.0/extra"):
            with self.subTest(tag=tag):
                result, notes, outputs = self.run_script(tag)
                self.assertNotEqual(result.returncode, 0)
                self.assertIn("Invalid version tag", result.stderr)
                self.assertIsNone(notes)
                self.assertIsNone(outputs)

    def test_empty_or_duplicate_section_fails(self):
        for changelog, message in (
            ("## V0.1.0\n\n## V0.0.1\nold", "Empty"),
            ("## V0.1.0\na\n## v0.1.0\nb", "Duplicate"),
        ):
            with self.subTest(message=message):
                result, notes, outputs = self.run_script("V0.1.0", changelog)
                self.assertNotEqual(result.returncode, 0)
                self.assertIn(message, result.stderr)
                self.assertIsNone(notes)
                self.assertIsNone(outputs)


if __name__ == "__main__":
    unittest.main()
