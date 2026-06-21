#!/usr/bin/env python3
from datetime import datetime, timezone
import sqlite3

path = "/srv/thaliak/db/thaliak.db"
now = datetime.now(timezone.utc).isoformat()

conn = sqlite3.connect(path)
cur = conn.cursor()
queued = cur.execute(
    "select count(*) from patches "
    "where notification_queued_at_utc is not null and notification_sent_at_utc is null"
).fetchone()[0]
sent = cur.execute(
    "select count(*) from patches where notification_sent_at_utc is not null"
).fetchone()[0]

cur.execute(
    "update patches set notification_sent_at_utc = ? "
    "where notification_queued_at_utc is not null and notification_sent_at_utc is null",
    (now,),
)
conn.commit()

after_queued = cur.execute(
    "select count(*) from patches "
    "where notification_queued_at_utc is not null and notification_sent_at_utc is null"
).fetchone()[0]
after_sent = cur.execute(
    "select count(*) from patches where notification_sent_at_utc is not null"
).fetchone()[0]
conn.close()

print(f"queued_before={queued}")
print(f"sent_before={sent}")
print(f"queued_after={after_queued}")
print(f"sent_after={after_sent}")
