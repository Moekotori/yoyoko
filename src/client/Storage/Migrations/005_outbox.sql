CREATE TABLE IF NOT EXISTS pending_sends (
    instance_id TEXT NOT NULL,
    account_id TEXT NOT NULL,
    channel_id TEXT NOT NULL,
    local_id TEXT NOT NULL,
    payload TEXT NOT NULL,
    PRIMARY KEY(instance_id, account_id, local_id)
);
CREATE INDEX IF NOT EXISTS pending_sends_channel
    ON pending_sends(instance_id, account_id, channel_id);
PRAGMA user_version = 5;
