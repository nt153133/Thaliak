#!/usr/bin/env python3
import sqlite3

conn = sqlite3.connect("/srv/thaliak/db/thaliak.db")
cur = conn.cursor()
print(
    "queued_unsent="
    + str(
        cur.execute(
            "select count(*) from patches "
            "where notification_queued_at_utc is not null and notification_sent_at_utc is null"
        ).fetchone()[0]
    )
)
print("discord_hooks=" + str(cur.execute("select count(*) from discord_hooks").fetchone()[0]))
if cur.execute(
    "select count(*) from sqlite_master where type = 'table' and name = 'installation_states'"
).fetchone()[0]:
    print("installation_states:")
    for row in cur.execute(
        "select r.name, s.status, coalesce(s.installed_version, ''), "
        "coalesce(s.last_error, '') "
        "from installation_states s "
        "join repositories r on r.id = s.repository_id "
        "order by r.service_id, r.id"
    ):
        print(" | ".join(str(value) for value in row))
conn.close()
