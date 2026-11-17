"""Persistent source identity, independent of mutable filenames and row numbers."""
import hashlib
import json
import uuid
from collections import defaultdict


def content_hash(record):
    data = json.dumps([record.get("Group", ""), record.get("Text", "")],
                      ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    return hashlib.sha256(data).hexdigest()


def document_id(record, source_files):
    source = record.get("Source", "")
    original = next((item for item in source_files if source.startswith(
        item["file"].removesuffix(".xlsx").removesuffix(".docx"))), None)
    key = original["sha256"] if original else record.get("StableId") or ""
    return str(uuid.uuid5(uuid.NAMESPACE_URL, "paperstudio:document:" + key))


def stamp_source_metadata(records, source_files, version="1.0"):
    for record in records:
        record["DocumentId"] = document_id(record, source_files)
        record["ChunkId"] = record["StableId"]
        record["IndexVersion"] = version
        record["SourceContentHash"] = content_hash(record)
    return records


def validate_source_metadata(records, source_files, version="1.0"):
    validate_stable_ids(records)
    for record in records:
        assert record.get("DocumentId") == document_id(record, source_files), "Missing or stale document ID: " + record["Id"]
        assert record.get("ChunkId") == record["StableId"], "Chunk ID differs from stable citation ID: " + record["Id"]
        assert record.get("IndexVersion") == version, "Stale index version: " + record["Id"]


def valid_identity(record):
    try:
        value = record["StableId"]
        return str(uuid.UUID(value)) == value and record.get("SourceContentHash") == content_hash(record)
    except (KeyError, ValueError, TypeError, AttributeError):
        return False


def assign_stable_ids(records, previous):
    """Reuse exact unique content; ambiguous duplicates require an unchanged alias.

    Content changes get new identities. No fuzzy matching or ordinal pairing is
    allowed: either could silently bind an existing identity to another record.
    """
    old, current = defaultdict(list), defaultdict(list)
    for record in previous:
        old[(record.get("Group", ""), record.get("Text", ""))].append(record)
    for record in records:
        current[(record.get("Group", ""), record.get("Text", ""))].append(record)
    used = set()
    for key, group in current.items():
        candidates = old[key]
        for record in group:
            if len(group) == len(candidates) == 1:
                matches = candidates
            else:
                matches = [item for item in candidates if item.get("Id") == record.get("Id")]
                if sum(item.get("Id") == record.get("Id") for item in group) != 1:
                    matches = []
            stable = matches[0]["StableId"] if len(matches) == 1 and valid_identity(matches[0]) else None
            if stable is None or stable in used:
                stable = str(uuid.uuid4())
            used.add(stable)
            record["StableId"] = stable
            record["SourceContentHash"] = content_hash(record)
    return records


def validate_stable_ids(records):
    assert all(valid_identity(record) for record in records), "Missing or stale source identity; rebuild the catalogue"
    assert len({record["StableId"] for record in records}) == len(records), "Duplicate stable source IDs"
