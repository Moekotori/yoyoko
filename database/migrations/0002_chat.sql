-- Phase 1: transactional outbox, resume sessions, pending uploads, invites.
ALTER TABLE attachments ALTER COLUMN message_id DROP NOT NULL;
ALTER TABLE attachments ADD COLUMN IF NOT EXISTS created_at TIMESTAMPTZ NOT NULL DEFAULT now();

CREATE TABLE IF NOT EXISTS invites (
    code TEXT PRIMARY KEY CHECK (length(code) BETWEEN 6 AND 16),
    server_id UUID NOT NULL REFERENCES servers(id) ON DELETE CASCADE,
    created_by UUID NOT NULL REFERENCES users(id),
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS invites_server ON invites(server_id);

CREATE TABLE IF NOT EXISTS event_outbox (
    seq BIGSERIAL PRIMARY KEY,
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    event TEXT NOT NULL,
    payload JSONB NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS event_outbox_user_seq ON event_outbox(user_id, seq);

CREATE TABLE IF NOT EXISTS gateway_sessions (
    id UUID PRIMARY KEY,
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    last_seq BIGINT NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    expires_at TIMESTAMPTZ NOT NULL
);
CREATE INDEX IF NOT EXISTS gateway_sessions_user ON gateway_sessions(user_id);

CREATE TABLE IF NOT EXISTS idempotency_keys (
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    key TEXT NOT NULL CHECK (length(key) BETWEEN 8 AND 128),
    message_id UUID NOT NULL REFERENCES messages(id) ON DELETE CASCADE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY(user_id, key)
);
