#!/usr/bin/env python3
import os
import sqlite3
from collections import Counter

patch_root = "/srv/thaliak/patches"
db_path = "/srv/thaliak/db/thaliak.db"

conn = sqlite3.connect(db_path)
conn.row_factory = sqlite3.Row
rows = conn.execute(
    """
    select p.id,
           p.local_storage_path,
           p.remote_origin_path,
           r.slug as repository_slug,
           s.name as service_name
    from patches p
    left join repo_versions rv on rv.id = p.repo_version_id
    left join repositories r on r.id = rv.repository_id
    left join services s on s.id = r.service_id
    order by p.id
    """
).fetchall()
conn.close()

missing = []
present = 0
for row in rows:
    local_path = row["local_storage_path"]
    if not local_path:
        missing.append(row)
        continue

    if os.path.exists(os.path.join(patch_root, local_path)):
        present += 1
    else:
        missing.append(row)

by_region = Counter((row["service_name"] or "unknown") for row in rows)
missing_by_region = Counter((row["service_name"] or "unknown") for row in missing)

print(f"patch_rows={len(rows)}")
print(f"downloaded_files_present={present}")
print(f"missing_files={len(missing)}")

print("patch_rows_by_service=" + ", ".join(f"{name}:{count}" for name, count in sorted(by_region.items())))
if missing:
    print("missing_by_service=" + ", ".join(f"{name}:{count}" for name, count in sorted(missing_by_region.items())))
    print("missing_examples:")
    for row in missing[:20]:
        print(f"- [{row['service_name'] or 'unknown'}] {row['remote_origin_path']} -> {row['local_storage_path']}")
