CREATE TABLE IF NOT EXISTS servers (
    instance_id TEXT NOT NULL,
    account_id TEXT NOT NULL,
    id TEXT NOT NULL,
    payload TEXT NOT NULL,
    PRIMARY KEY(instance_id, account_id, id)
);
CREATE TABLE IF NOT EXISTS channels (
    instance_id TEXT NOT NULL,
    account_id TEXT NOT NULL,
    id TEXT NOT NULL,
    server_id TEXT NOT NULL,
    payload TEXT NOT NULL,
    PRIMARY KEY(instance_id, account_id, id)
);
CREATE TABLE IF NOT EXISTS users (
    instance_id TEXT NOT NULL,
    account_id TEXT NOT NULL,
    id TEXT NOT NULL,
    payload TEXT NOT NULL,
    PRIMARY KEY(instance_id, account_id, id)
);
CREATE TABLE IF NOT EXISTS sync_state (
    instance_id TEXT NOT NULL,
    account_id TEXT NOT NULL,
    session_id TEXT,
    last_committed_seq INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY(instance_id, account_id)
);
PRAGMA user_version = 2;
