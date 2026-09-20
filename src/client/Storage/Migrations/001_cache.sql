CREATE TABLE IF NOT EXISTS instances (
    id TEXT PRIMARY KEY, base_url TEXT NOT NULL UNIQUE, display_name TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS accounts (
    instance_id TEXT NOT NULL REFERENCES instances(id) ON DELETE CASCADE,
    id TEXT NOT NULL, display_name TEXT NOT NULL,
    PRIMARY KEY(instance_id, id)
);
-- Rows are partitioned by both instance AND signed-in account.
CREATE TABLE IF NOT EXISTS messages (
    instance_id TEXT NOT NULL REFERENCES instances(id) ON DELETE CASCADE,
    account_id TEXT NOT NULL, channel_id TEXT NOT NULL, id TEXT NOT NULL,
    payload TEXT NOT NULL,
    PRIMARY KEY(instance_id, account_id, id)
);
CREATE INDEX IF NOT EXISTS messages_page ON messages(instance_id, account_id, channel_id, id DESC);
CREATE TABLE IF NOT EXISTS settings (
    key TEXT PRIMARY KEY, value TEXT NOT NULL
);
PRAGMA user_version = 1;
