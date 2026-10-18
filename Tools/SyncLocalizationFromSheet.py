#!/usr/bin/env python3
"""Validate a connected Sheets export, then optionally apply translations locally.

Network access is handled by the authenticated Google Drive connector, or by
downloading the Strings tab as CSV. Requires only Python 3.9+ standard library.
"""

import argparse
from collections import Counter
import csv
from datetime import datetime, timezone
import io
import json
import os
from pathlib import Path
import re
import sys
import tempfile


HEADERS = ["source", "ko", "ja", "zh-Hans"]
TOKEN = re.compile(r"\{[0-9]+(?:,-?[0-9]+)?(?::[^{}]*)?\}")


class ValidationError(ValueError):
    pass


def decode_cell(value):
    # Matches StringTable.Unescape; leave unknown backslash sequences intact.
    value = re.sub(r"\\(n|\\)", lambda m: "\n" if m[1] == "n" else "\\", value)
    return value.replace("\r\n", "\n").replace("\r", "\n")


def encode_cell(value):
    return value.replace("\\", "\\\\").replace("\n", "\\n")


def placeholders(value):
    tokens = []
    index = 0
    while index < len(value):
        if value[index:index + 2] in ("{{", "}}"):
            index += 2
        elif value[index] == "{":
            match = TOKEN.match(value, index)
            if not match:
                raise ValidationError("Invalid placeholder or unescaped brace: " + repr(value))
            tokens.append(match[0])
            index = match.end()
        elif value[index] == "}":
            raise ValidationError("Unescaped closing brace: " + repr(value))
        else:
            index += 1
    return Counter(tokens)


def validate_rows(rows):
    if not isinstance(rows, list) or not rows or rows[0] != HEADERS:
        raise ValidationError("Header must be exactly: source,ko,ja,zh-Hans")
    table = {}
    for number, row in enumerate(rows[1:], 2):
        if not isinstance(row, list) or any(not isinstance(v, str) for v in row):
            raise ValidationError(f"Row {number}: every cell must be plain text")
        if not any(row):
            continue
        if len(row) != 4:
            raise ValidationError(f"Row {number}: expected four non-empty columns")
        source, *translations = [decode_cell(v) for v in row]
        if not source.strip():
            raise ValidationError(f"Row {number}: empty source")
        if source in table:
            raise ValidationError(f"Row {number}: duplicate source {source!r}")
        expected = placeholders(source)
        for language, value in zip(HEADERS[1:], translations):
            if not value.strip():
                raise ValidationError(f"Row {number} ({language}): empty translation")
            if placeholders(value) != expected:
                raise ValidationError(f"Row {number} ({language}): placeholders differ from source")
            if value.count("\n") != source.count("\n"):
                raise ValidationError(f"Row {number} ({language}): line breaks differ from source")
        table[source] = translations
    if not table:
        raise ValidationError("The table contains no entries")
    return table


def csv_rows(data):
    return list(csv.reader(io.StringIO(data.decode("utf-8-sig"), newline=""), strict=True))


def read_input(path):
    data = path.read_bytes()
    if path.suffix.lower() == ".json":
        payload = json.loads(data.decode("utf-8-sig"))
        if isinstance(payload, dict):
            if payload.get("majorDimension", "ROWS") != "ROWS":
                raise ValidationError("JSON majorDimension must be ROWS")
            payload = payload.get("values")
        return validate_rows(payload)
    if path.suffix.lower() != ".csv":
        raise ValidationError("Input must be a UTF-8 CSV or JSON values export")
    return validate_rows(csv_rows(data))


def compare_tables(current, incoming):
    missing = current.keys() - incoming.keys()
    extra = incoming.keys() - current.keys()
    if missing or extra:
        raise ValidationError(
            f"Source keys must match the local CSV (case sensitive). "
            f"Missing: {len(missing)} {sorted(missing)[:3]!r}; "
            f"unexpected: {len(extra)} {sorted(extra)[:3]!r}")
    return [(source, language, before, after)
            for source, values in current.items()
            for language, before, after in zip(HEADERS[1:], values, incoming[source])
            if before != after]


def serialize_table(current, incoming, original):
    # Keep local row order even when someone sorts the Google Sheet.
    stream = io.StringIO(newline="")
    writer = csv.writer(stream, lineterminator="\r\n" if b"\r\n" in original else "\n")
    writer.writerow(HEADERS)
    for source in current:
        writer.writerow([encode_cell(v) for v in [source, *incoming[source]]])
    encoding = "utf-8-sig" if original.startswith(b"\xef\xbb\xbf") else "utf-8"
    return stream.getvalue().encode(encoding)


def replace_with_backup(target, original, updated, backup_dir):
    if target.read_bytes() != original:
        raise ValidationError("Local CSV changed during validation; read it again before applying")
    backup_dir.mkdir(parents=True, exist_ok=True)
    stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
    backup = backup_dir / ("StringTable-" + stamp + ".csv")
    with backup.open("xb") as stream:
        stream.write(original)
    temporary = None
    try:
        # Same directory ensures replace is on the same filesystem. Never touch .meta.
        with tempfile.NamedTemporaryFile(dir=target.parent, prefix=".StringTable-",
                                         suffix=".tmp", delete=False) as stream:
            temporary = Path(stream.name)
            stream.write(updated)
            stream.flush()
            os.fsync(stream.fileno())
        if target.read_bytes() != original:
            raise ValidationError("Local CSV changed before replacement; no changes applied")
        os.replace(temporary, target)
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)
    return backup


def sync_file(target, input_path, backup_dir, apply=False):
    original = target.read_bytes()
    current = validate_rows(csv_rows(original))
    incoming = read_input(input_path)
    changes = compare_tables(current, incoming)
    backup = None
    if apply and changes:
        updated = serialize_table(current, incoming, original)
        # Verify serialized CSV before any mutation of the game asset.
        if validate_rows(csv_rows(updated)) != incoming:
            raise ValidationError("Serialized CSV did not round-trip")
        backup = replace_with_backup(target, original, updated, backup_dir)
    return len(current), changes, backup


def main():
    # Redirected Windows terminals must also be able to show Japanese/Chinese diffs.
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", required=True, type=Path, help="Downloaded CSV or JSON values")
    parser.add_argument("--apply", action="store_true", help="Apply after validation; default is dry run")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    try:
        config = json.loads((root / "Tools/LocalizationSheet.json").read_text(encoding="utf-8"))
        target = (root / config["localCsvPath"]).resolve()
        if root not in target.parents or config["columns"] != HEADERS:
            raise ValidationError("Invalid localization configuration")
        count, changes, backup = sync_file(target, args.input, root / "Logs/LocalizationSync", args.apply)
        print(f"Validated {count} entries; {len(changes)} changed translation cells.")
        for source, language, before, after in changes[:20]:
            print(f"  {language} | {source[:100]!r}: {before!r} -> {after!r}")
        if len(changes) > 20:
            print(f"  ... {len(changes) - 20} more changed cells; review the CSV diff after applying.")
        if backup:
            print(f"Applied. Backup: {backup}")
        elif not changes:
            print("No changes. Local CSV bytes preserved.")
        else:
            print("Dry run only. Add --apply to write these validated translations.")
        return 0
    except (OSError, ValueError, csv.Error, KeyError) as error:
        print(f"Localization sync failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
