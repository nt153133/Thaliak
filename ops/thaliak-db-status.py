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
conn.close()
