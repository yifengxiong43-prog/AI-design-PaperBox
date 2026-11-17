"""Read-only, record-level integrity report for the embedded packaging corpus."""
import argparse
import hashlib
import json
import sys
import uuid
import zipfile
from collections import Counter, defaultdict
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from stable_source_ids import content_hash, document_id

ROOT = Path(__file__).parent / "resources"


def digest(data):
    return hashlib.sha256(data).hexdigest()


def load(name):
    return json.loads((ROOT / name).read_text(encoding="utf-8"))


def diagnose(original_dir=None, task_path=None):
    records = load("knowledge.json") + load("web-knowledge.json")
    analysis = load("knowledge-analysis.json")
    manifest = load("source-manifest.json")
    entries = defaultdict(list)
    for entry in analysis["Entries"]:
        entries[entry["SourceId"]].append(entry)
    aliases = Counter(row.get("Id") for row in records)
    stable_ids = Counter(row.get("StableId") for row in records)
    issues = []

    def issue(code, record_id, source, repairable, detail):
        issues.append(dict(type=code, record_id=record_id, file=source,
                           auto_repairable=repairable, reason=detail))

    files = {item["file"]: item for item in manifest["sources"]}
    embedded = {}
    archive_path = ROOT / "original-sources.zip"
    with zipfile.ZipFile(archive_path) as archive:
        for name, item in files.items():
            try:
                data = archive.read(name)
                embedded[name] = data
                if digest(data) != item["sha256"] or len(data) != item["bytes"]:
                    issue("original_hash_mismatch", None, name, False, "Bundled original differs from manifest")
                if original_dir:
                    disk = original_dir / name
                    if not disk.exists():
                        issue("user_original_missing", None, str(disk), False, "Bundled original remains available")
                    elif digest(disk.read_bytes()) != item["sha256"]:
                        issue("user_original_changed", None, str(disk), False, "User file differs from bundled original")
            except KeyError:
                issue("embedded_original_missing", None, name, False, "Original absent from archive")

    for document in load("full-reference-extract.json"):
        name = document["file"]
        if name not in files or document.get("sha256") != files[name]["sha256"]:
            issue("extraction_version_mismatch", None, name, True, "Extract does not match bundled original hash")
            continue
        document_text = "\n".join(row.get("Text", "") for row in records
                                  if row.get("Source", "").startswith(name.removesuffix(".xlsx").removesuffix(".docx")))
        values = (paragraph for part in document.get("parts", []) for paragraph in part.get("paragraphs", []))
        if "sheets" in document:
            values = (cell.get("value", "") for sheet in document["sheets"] for cell in sheet["cells"])
        for value in values:
            if value.startswith('=_xlfn.DISPIMG('):
                continue
            if value and value.strip() and value.strip() not in document_text:
                issue("original_value_not_indexed", None, name, True, f"original text={value[:100]}")

    record_ids = {row.get("Id") for row in records}
    source_hash = digest((ROOT / "knowledge.json").read_bytes() + (ROOT / "web-knowledge.json").read_bytes())
    if source_hash != analysis.get("CorpusHash"):
        issue("stale_index", None, "knowledge-analysis.json", True, "Corpus hash differs from current source data")
    for row in records:
        alias, stable, source = row.get("Id"), row.get("StableId"), row.get("Source", "")
        parent = next((name for name in files if source.startswith(name.removesuffix(".xlsx").removesuffix(".docx"))), None)
        if not alias or aliases[alias] > 1:
            issue("duplicate_or_empty_record_id", alias, source, False, f"occurrences={aliases[alias]}")
        if not stable or stable_ids[stable] > 1:
            issue("duplicate_or_empty_chunk_id", alias, source, False, f"chunk_id={stable}; occurrences={stable_ids[stable]}")
        if row.get("ChunkId") != stable:
            issue("chunk_id_mismatch", alias, source, True, "Chunk ID differs from stable citation ID")
        if row.get("DocumentId") != document_id(row, manifest["sources"]):
            issue("document_id_mismatch", alias, source, True, "Document ID differs from verified original identity")
        if row.get("IndexVersion") != analysis.get("Version"):
            issue("record_index_version_mismatch", alias, source, True, "Record index version differs from analysis")
        try:
            if str(uuid.UUID(stable)) != stable:
                raise ValueError("non-canonical UUID")
        except (ValueError, TypeError, AttributeError):
            issue("invalid_chunk_id", alias, source, False, f"chunk_id={stable}")
        if not isinstance(row.get("Text"), str) or not row["Text"].strip():
            issue("source_text_missing", alias, source, False, "Cannot reconstruct text from summary")
        elif row.get("SourceContentHash") != content_hash(row):
            issue("content_hash_mismatch", alias, source, True, "Stored hash does not match raw text")
        if parent and parent not in embedded:
            issue("source_original_missing", alias, source, False, "Original file is not available")
        if len(entries[alias]) != 1:
            issue("index_record_mismatch", alias, source, True, f"index_entries={len(entries[alias])}")
        else:
            entry = entries[alias][0]
            for fact in entry.get("Facts", []):
                if fact.get("Quote") not in (row.get("Text") or ""):
                    issue("quote_not_in_original", alias, source, True, f"quote={fact.get('Quote', '')[:100]}")
            if entry.get("Summary") and not any(
                fact.get("Quote", "") in row.get("Text", "") for fact in entry.get("Facts", [])
            ):
                issue("summary_without_original_evidence", alias, source, True, "No indexed fact matches source text")
    for alias in entries:
        if alias not in record_ids:
            issue("orphan_index_record", alias, "knowledge-analysis.json", True, "No matching source record")
    for image in analysis.get("Images", []):
        path = ROOT / image["Resource"]
        if not path.exists() or digest(path.read_bytes()) != image["Sha256"]:
            issue("image_missing_or_changed", image["Id"], str(path), False, "Image cannot be verified")
    for rule in analysis.get("Rules", []):
        for alias in rule.get("SourceIds", []):
            if alias not in record_ids:
                issue("rule_source_missing", alias, rule.get("Id"), True, "Rule refers to missing source")

    task = None
    if task_path and task_path.exists():
        task = json.loads(task_path.read_text(encoding="utf-8-sig"))
        snapshots = task.get("RetrievedSources") or []
        by_stable = {row.get("StableId"): row for row in records}
        for snapshot in snapshots:
            current = by_stable.get(snapshot.get("StableId"))
            if current is None or current.get("Text") != snapshot.get("Text"):
                issue("stale_task_source", snapshot.get("Id"), snapshot.get("Source"), True,
                      "Task must re-run retrieval; prior citation cannot be reused")
        if task.get("Status") == "completed" and any(s.get("Status") == "failed" for s in task.get("Steps", [])):
            issue("inconsistent_task_status", task.get("TaskId"), str(task_path), True, "Completed task has failed step")

    serious = {"duplicate_or_empty_record_id", "duplicate_or_empty_chunk_id", "invalid_chunk_id",
               "source_text_missing", "source_original_missing", "embedded_original_missing", "original_hash_mismatch",
               "chunk_id_mismatch", "document_id_mismatch", "record_index_version_mismatch"}
    bad_ids = {item["record_id"] for item in issues if item["type"] in serious}
    repeated_content = defaultdict(list)
    for row in records:
        repeated_content[(row.get("Group"), row.get("Text"))].append(row.get("Id"))
    identity = []
    for row in records:
        source = row.get("Source", "")
        identity.append(dict(document_id=row.get("DocumentId"),
                             chunk_id=row.get("StableId"), legacy_id=row.get("Id"), source_path=source,
                             content_hash=row.get("SourceContentHash"), index_version=analysis.get("Version")))
    return dict(total_records=len(records), original_records=len(load("knowledge.json")),
                supplemental_records=len(load("web-knowledge.json")), valid_records=len(records)-len(bad_ids),
                duplicate_legacy_ids=sum(n-1 for n in aliases.values() if n > 1),
                duplicate_chunk_ids=sum(n-1 for n in stable_ids.values() if n > 1),
                missing_original_records=len({i["record_id"] for i in issues if i["type"] in
                                              ("source_original_missing", "source_text_missing")}),
                index_version=analysis.get("Version"), corpus_hash=source_hash,
                task_id=task.get("TaskId") if task else None, issues=issues, identities=identity,
                repeated_content_groups=[ids for ids in repeated_content.values() if len(ids) > 1])


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--original-dir", type=Path)
    parser.add_argument("--task", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    report = diagnose(args.original_dir, args.task)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({key: value for key, value in report.items() if key not in ("identities", "issues", "repeated_content_groups")}
                     | {"repeated_content_groups": len(report["repeated_content_groups"])}
                     | {"issues": len(report["issues"]), "issue_types": dict(Counter(i["type"] for i in report["issues"]))},
                     ensure_ascii=False))
