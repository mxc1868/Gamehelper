#!/usr/bin/env python3
"""Convert a local RePoE2 uniques.json export to full-art-path candidate lists (no network)."""
import argparse
import json
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input", type=Path, help="Pinned RePoE2 data/uniques.json")
    parser.add_argument("--output", type=Path, default=Path(__file__).resolve().parent.parent /
                        "Plugins/UniqueLoot/Data/uniqueArtMapping.json")
    args = parser.parse_args()
    rows = json.loads(args.input.read_text(encoding="utf-8"))
    mapping = {}
    for row in rows.values():
        art = (row.get("visual_identity") or {}).get("dds_file")
        name = (row.get("name") or "").strip()
        if not art or not name:
            continue
        if not art.startswith("Art/2DItems/") or not art.endswith(".dds"):
            raise ValueError("Unexpected art path: " + art)
        mapping.setdefault(art, set()).add(name)
    if not mapping:
        raise ValueError("Export contains no usable unique art mappings")
    result = {key: sorted(names) for key, names in sorted(mapping.items())}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"{len(result)} art paths; {len(set().union(*mapping.values()))} distinct names; "
          f"{sum(len(names) > 1 for names in mapping.values())} ambiguous paths")


if __name__ == "__main__":
    main()
