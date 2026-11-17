"""Validate identity migration without opening or changing user documents."""
from copy import deepcopy
from stable_source_ids import assign_stable_ids, validate_stable_ids


def row(alias, text="same"):
    return {"Id": alias, "Group": "市场参考", "Text": text}


def check():
    original = assign_stable_ids([row("file/sheet/7"), row("file/sheet/8", "other")], [])
    rerun = assign_stable_ids([row("file/sheet/7"), row("file/sheet/8", "other")], original)
    assert [r["StableId"] for r in rerun] == [r["StableId"] for r in original]
    moved = assign_stable_ids([row("renamed/sheet/9"), row("renamed/sheet/3", "other")], original)
    assert [r["StableId"] for r in moved] == [r["StableId"] for r in original]
    changed = assign_stable_ids([row("file/sheet/7", "different")], original)
    assert changed[0]["StableId"] not in {r["StableId"] for r in original}
    duplicates = assign_stable_ids([row("a"), row("b")], [])
    validate_stable_ids(duplicates)
    rerun_duplicates = assign_stable_ids([row("b"), row("a")], duplicates)
    assert {r["Id"]: r["StableId"] for r in duplicates} == {r["Id"]: r["StableId"] for r in rerun_duplicates}
    ambiguous = assign_stable_ids([row("c"), row("d")], duplicates)
    assert not {r["StableId"] for r in ambiguous} & {r["StableId"] for r in duplicates}
    edited = deepcopy(original)
    edited[0]["Text"] = "changed in place"
    upgraded = assign_stable_ids(deepcopy(edited), edited)
    assert upgraded[0]["StableId"] != original[0]["StableId"]
    assert upgraded[1]["StableId"] == original[1]["StableId"]
    validate_stable_ids(upgraded)
    print("PASS: identical reimport, filename/row moves, changed text, duplicates and stale identity")


if __name__ == "__main__":
    check()
