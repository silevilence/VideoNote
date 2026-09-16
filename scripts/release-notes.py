"""Extract one changelog section before publishing a version tag (no dependencies)."""

import argparse
import os
from pathlib import Path
import re
import sys


def extract_notes(changelog: str, tag: str) -> tuple[str, str]:
    if not re.fullmatch(r"[vV][0-9]+\.[0-9]+\.[0-9]+", tag):
        raise ValueError(f"Invalid version tag {tag!r}; expected V<major>.<minor>.<patch>.")

    version = tag[1:]
    headings = list(re.finditer(r"^##[ \t]+([^\r\n]+)", changelog, re.MULTILINE))
    matches = [i for i, heading in enumerate(headings)
               if heading.group(1).strip().casefold() == tag.casefold()]
    if not matches:
        raise ValueError(f"Missing changelog section for version {version} (expected ## V{version}).")
    if len(matches) != 1:
        raise ValueError(f"Duplicate changelog sections for version {version}.")

    index = matches[0]
    heading = headings[index]
    end = headings[index + 1].start() if index + 1 < len(headings) else len(changelog)
    if not changelog[heading.end():end].strip():
        raise ValueError(f"Empty changelog section for version {version}.")
    return version, changelog[heading.start():end].strip() + "\n"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("tag")
    parser.add_argument("--changelog", type=Path, default=Path("changelog.md"))
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        version, notes = extract_notes(args.changelog.read_text(encoding="utf-8-sig"), args.tag)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(notes, encoding="utf-8", newline="\n")
        if output_file := os.environ.get("GITHUB_OUTPUT"):
            with open(output_file, "a", encoding="utf-8") as stream:
                stream.write(f"version={version}\n")
        print(f"Validated changelog for version {version}; notes written to {args.output}.")
        return 0
    except (OSError, ValueError) as error:
        print(f"Release validation failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
