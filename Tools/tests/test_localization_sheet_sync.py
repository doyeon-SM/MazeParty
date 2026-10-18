import copy
import csv
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock


spec = importlib.util.spec_from_file_location(
    "localization_sync", Path(__file__).resolve().parents[1] / "SyncLocalizationFromSheet.py")
sync = importlib.util.module_from_spec(spec)
spec.loader.exec_module(sync)


class LocalizationSyncTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.target = self.root / "StringTable.csv"
        self.meta = self.root / "StringTable.csv.meta"
        self.meta.write_bytes(b"guid: do-not-change\n")
        self.input = self.root / "sheet.json"
        self.backups = self.root / "backups"
        self.rows = [sync.HEADERS,
                     ["Close", "닫기", "閉じる", "关闭"],
                     ["CLOSE", "닫기", "閉じる", "关闭"],
                     [r'Value {0:0.0}\n"path", \\n', r'값 {0:0.0}\n"경로", \\n',
                      r'値 {0:0.0}\n"経路", \\n', r'值 {0:0.0}\n"路径", \\n']]
        stream = io.StringIO(newline="")
        csv.writer(stream, lineterminator="\r\n").writerows(self.rows)
        self.original = b"\xef\xbb\xbf" + stream.getvalue().encode("utf-8") + b"\r\n"
        self.target.write_bytes(self.original)

    def export(self, rows):
        self.input.write_text(json.dumps({"majorDimension": "ROWS", "values": rows}), encoding="utf-8")

    def run_sync(self, apply=True):
        return sync.sync_file(self.target, self.input, self.backups, apply)

    def test_invalid_exports_cannot_damage_csv_or_meta(self):
        cases = []
        for label, change in [
            ("header", lambda r: r[0].reverse()),
            ("duplicate", lambda r: r.append(r[1][:])),
            ("missing key", lambda r: r.pop()),
            ("changed key", lambda r: r[1].__setitem__(0, "close")),
            ("blank source", lambda r: r[1].__setitem__(0, "")),
            ("blank translation", lambda r: r[1].__setitem__(1, "")),
            ("numeric cell", lambda r: r[1].__setitem__(1, 123)),
            ("missing placeholder", lambda r: r[3].__setitem__(1, r"값\n경로")),
            ("invalid format", lambda r: r[3].__setitem__(1, r"값 {0\n경로")),
            ("changed line count", lambda r: r[3].__setitem__(1, "값 {0:0.0}")),
            ("missing column", lambda r: r[1].pop()),
        ]:
            rows = copy.deepcopy(self.rows)
            change(rows)
            cases.append((label, rows))
        for label, rows in cases:
            with self.subTest(label=label):
                self.export(rows)
                with self.assertRaises(sync.ValidationError):
                    self.run_sync()
                self.assertEqual(self.original, self.target.read_bytes())
                self.assertEqual(b"guid: do-not-change\n", self.meta.read_bytes())
                self.assertFalse(self.backups.exists())

    def test_sorted_unchanged_sheet_preserves_exact_local_bytes(self):
        self.export([self.rows[0], *reversed(self.rows[1:])])
        count, changes, backup = self.run_sync()
        self.assertEqual(3, count)
        self.assertEqual([], changes)
        self.assertIsNone(backup)
        self.assertEqual(self.original, self.target.read_bytes())

    def test_apply_round_trips_text_and_keeps_row_order_encoding_and_meta(self):
        rows = copy.deepcopy(self.rows)
        rows[1][1] = '창 닫기, "완료"'
        # Raw Sheets line break and an escaped literal backslash both survive.
        rows[3][1] = '수치 {0:0.0}\n"경로", ' + r'\\n'
        rows = [rows[0], *reversed(rows[1:])]
        self.export(rows)
        for bom, newline in [(b"", b"\n"), (b"\xef\xbb\xbf", b"\r\n")]:
            with self.subTest(newline=newline):
                original = bom + self.original[3:].replace(b"\r\n", newline)
                self.target.write_bytes(original)
                _, changes, backup = self.run_sync(apply=False)
                self.assertEqual(2, len(changes))
                self.assertIsNone(backup)
                self.assertEqual(original, self.target.read_bytes())
                _, _, backup = self.run_sync()
                self.assertEqual(original, backup.read_bytes())
                output = self.target.read_bytes()
                self.assertEqual(bool(bom), output.startswith(b"\xef\xbb\xbf"))
                self.assertEqual(sync.validate_rows(rows), sync.validate_rows(sync.csv_rows(output)))
                self.assertEqual([r[0] for r in self.rows[1:]], [r[0] for r in sync.csv_rows(output)[1:]])
                self.assertEqual(b"guid: do-not-change\n", self.meta.read_bytes())

    def test_failed_replacement_retains_original_and_recoverable_backup(self):
        rows = copy.deepcopy(self.rows)
        rows[1][1] = "창 닫기"
        self.export(rows)
        with mock.patch.object(sync.os, "replace", side_effect=OSError("file locked")):
            with self.assertRaises(OSError):
                self.run_sync()
        self.assertEqual(self.original, self.target.read_bytes())
        self.assertEqual(self.original, next(self.backups.glob("*.csv")).read_bytes())
        self.assertEqual([], list(self.root.glob(".StringTable-*.tmp")))

    def test_concurrent_local_change_is_not_overwritten(self):
        rows = copy.deepcopy(self.rows)
        rows[1][1] = "창 닫기"
        self.export(rows)
        reader = sync.read_input
        changed = self.original + b"\r\n"

        def read_then_change(path):
            result = reader(path)
            self.target.write_bytes(changed)
            return result

        with mock.patch.object(sync, "read_input", side_effect=read_then_change):
            with self.assertRaises(sync.ValidationError):
                self.run_sync()
        self.assertEqual(changed, self.target.read_bytes())
        self.assertFalse(self.backups.exists())

    def test_csv_export_and_runtime_equivalent_duplicate_keys(self):
        self.input = self.root / "sheet.csv"
        self.input.write_bytes(self.original)
        self.assertEqual([], self.run_sync()[1])
        duplicate = [sync.HEADERS,
                     [r"A\nB", r"가\n나", r"あ\nい", r"甲\n乙"],
                     ["A\nB", "가\n나", "あ\nい", "甲\n乙"]]
        with self.assertRaises(sync.ValidationError):
            sync.validate_rows(duplicate)


if __name__ == "__main__":
    unittest.main()
