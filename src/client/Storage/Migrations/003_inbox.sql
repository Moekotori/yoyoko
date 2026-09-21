CREATE TABLE IF NOT EXISTS channel_inbox (
    instance_id TEXT NOT NULL,
    account_id TEXT NOT NULL,
    channel_id TEXT NOT NULL,
    last_read_id TEXT,
    last_message_id TEXT,
    last_author_id TEXT,
    last_mention_id TEXT,
    notify TEXT NOT NULL DEFAULT 'all',
    draft TEXT,
    PRIMARY KEY(instance_id, account_id, channel_id)
);
CREATE INDEX IF NOT EXISTS channel_inbox_account
    ON channel_inbox(instance_id, account_id);
PRAGMA user_version = 3;
