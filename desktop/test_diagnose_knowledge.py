"""Fault injection for the read-only corpus diagnostic."""
import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import diagnose_knowledge as diagnostic


class KnowledgeDiagnosticTests(unittest.TestCase):
    def test_current_corpus_and_saved_task(self):
        report = diagnostic.diagnose()
        self.assertEqual(report["total_records"], 276)
        self.assertEqual(report["valid_records"], 276)
        self.assertEqual(report["issues"], [])

    def test_duplicate_and_missing_text_are_identified(self):
        original_load = diagnostic.load

        def broken_load(name):
            value = original_load(name)
            if name == "knowledge.json":
                value[1]["Id"] = value[0]["Id"]
                value[1]["StableId"] = value[0]["StableId"]
                value[1]["Text"] = ""
            return value

        with patch.object(diagnostic, "load", side_effect=broken_load):
            report = diagnostic.diagnose()
        kinds = {item["type"] for item in report["issues"]}
        self.assertTrue({"duplicate_or_empty_record_id", "duplicate_or_empty_chunk_id",
                         "source_text_missing"} <= kinds)
        self.assertLess(report["valid_records"], report["total_records"])

    def test_stale_task_source_is_not_treated_as_valid(self):
        source = diagnostic.load("knowledge.json")[0].copy()
        source["Text"] += "changed"
        with tempfile.TemporaryDirectory() as directory:
            task = Path(directory) / "task.json"
            task.write_text(json.dumps({"TaskId": "test", "Status": "failed",
                                        "RetrievedSources": [source], "Steps": []}), encoding="utf-8")
            report = diagnostic.diagnose(task_path=task)
        self.assertIn("stale_task_source", {item["type"] for item in report["issues"]})


if __name__ == "__main__":
    unittest.main()
