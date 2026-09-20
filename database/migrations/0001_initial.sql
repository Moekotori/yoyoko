-- Phase 0 schema foundation. UUIDv7 IDs are allocated by application services.
CREATE TABLE users (
    id UUID PRIMARY KEY,
    username TEXT NOT NULL UNIQUE CHECK (length(username) BETWEEN 2 AND 64),
    display_name TEXT NOT NULL CHECK (length(display_name) BETWEEN 1 AND 100),
    password_hash TEXT NOT NULL, -- Argon2id PHC string; auth implementation in Phase 1.
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE TABLE sessions (
    id UUID PRIMARY KEY,
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    refresh_token_hash BYTEA NOT NULL UNIQUE,
    expires_at TIMESTAMPTZ NOT NULL,
    revoked_at TIMESTAMPTZ
);
CREATE TABLE servers (
    id UUID PRIMARY KEY,
    owner_id UUID NOT NULL REFERENCES users(id),
    name TEXT NOT NULL CHECK (length(name) BETWEEN 1 AND 100),
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE TABLE members (
    server_id UUID NOT NULL REFERENCES servers(id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    joined_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY(server_id, user_id)
);
CREATE TABLE roles (
    id UUID PRIMARY KEY,
    server_id UUID NOT NULL REFERENCES servers(id) ON DELETE CASCADE,
    name TEXT NOT NULL,
    -- NUMERIC supports all u64 bits; signed BIGINT does not.
    permissions NUMERIC(20,0) NOT NULL CHECK (permissions BETWEEN 0 AND 18446744073709551615),
    is_base BOOLEAN NOT NULL DEFAULT false,
    UNIQUE(server_id,id)
);
CREATE UNIQUE INDEX one_base_role ON roles(server_id) WHERE is_base;
CREATE TABLE member_roles (
    server_id UUID NOT NULL, user_id UUID NOT NULL, role_id UUID NOT NULL,
    PRIMARY KEY(server_id,user_id,role_id),
    FOREIGN KEY(server_id,user_id) REFERENCES members(server_id,user_id) ON DELETE CASCADE,
    FOREIGN KEY(server_id,role_id) REFERENCES roles(server_id,id) ON DELETE CASCADE
);
CREATE TABLE channels (
    id UUID PRIMARY KEY,
    server_id UUID NOT NULL REFERENCES servers(id) ON DELETE CASCADE,
    name TEXT NOT NULL CHECK (length(name) BETWEEN 1 AND 100),
    kind TEXT NOT NULL CHECK (kind IN ('text','voice')),
    position INTEGER NOT NULL DEFAULT 0,
    UNIQUE(server_id,id)
);
CREATE INDEX channels_server ON channels(server_id, position);
CREATE TABLE channel_role_overrides (
    server_id UUID NOT NULL, channel_id UUID NOT NULL, role_id UUID NOT NULL,
    allow_bits NUMERIC(20,0) NOT NULL CHECK (allow_bits BETWEEN 0 AND 18446744073709551615),
    deny_bits NUMERIC(20,0) NOT NULL CHECK (deny_bits BETWEEN 0 AND 18446744073709551615),
    PRIMARY KEY(channel_id,role_id),
    FOREIGN KEY(server_id,channel_id) REFERENCES channels(server_id,id) ON DELETE CASCADE,
    FOREIGN KEY(server_id,role_id) REFERENCES roles(server_id,id) ON DELETE CASCADE
);
CREATE TABLE channel_member_overrides (
    server_id UUID NOT NULL, channel_id UUID NOT NULL, user_id UUID NOT NULL,
    allow_bits NUMERIC(20,0) NOT NULL CHECK (allow_bits BETWEEN 0 AND 18446744073709551615),
    deny_bits NUMERIC(20,0) NOT NULL CHECK (deny_bits BETWEEN 0 AND 18446744073709551615),
    PRIMARY KEY(channel_id,user_id),
    FOREIGN KEY(server_id,channel_id) REFERENCES channels(server_id,id) ON DELETE CASCADE,
    FOREIGN KEY(server_id,user_id) REFERENCES members(server_id,user_id) ON DELETE CASCADE
);
CREATE TABLE messages (
    id UUID PRIMARY KEY,
    channel_id UUID NOT NULL REFERENCES channels(id) ON DELETE CASCADE,
    author_id UUID NOT NULL REFERENCES users(id),
    kind TEXT NOT NULL CHECK (kind IN ('text','system','encrypted')),
    content TEXT CHECK (octet_length(content) <= 16384),
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    edited_at TIMESTAMPTZ,
    deleted_at TIMESTAMPTZ,
    reply_to UUID,
    encrypted_payload JSONB,
    embeds JSONB NOT NULL DEFAULT '[]',
    UNIQUE(channel_id,id),
    FOREIGN KEY(channel_id,reply_to) REFERENCES messages(channel_id,id),
    CHECK ((kind = 'encrypted' AND encrypted_payload IS NOT NULL AND content IS NULL)
       OR (kind <> 'encrypted' AND encrypted_payload IS NULL))
);
CREATE INDEX messages_page ON messages(channel_id, id DESC);
CREATE INDEX messages_search ON messages USING gin(to_tsvector('simple', coalesce(content,''))) WHERE deleted_at IS NULL;
CREATE TABLE message_mentions (
    message_id UUID NOT NULL REFERENCES messages(id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    PRIMARY KEY(message_id,user_id)
);
CREATE TABLE attachments (
    id UUID PRIMARY KEY,
    message_id UUID NOT NULL REFERENCES messages(id) ON DELETE CASCADE,
    uploader_id UUID NOT NULL REFERENCES users(id),
    object_key UUID NOT NULL UNIQUE,
    file_name TEXT NOT NULL CHECK (length(file_name) <= 255),
    mime_type TEXT NOT NULL,
    size_bytes BIGINT NOT NULL CHECK (size_bytes > 0 AND size_bytes <= 26214400),
    thumbnail_key UUID
);
CREATE TABLE reactions (
    message_id UUID NOT NULL REFERENCES messages(id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    emoji TEXT NOT NULL CHECK (length(emoji) BETWEEN 1 AND 64),
    PRIMARY KEY(message_id,user_id,emoji)
);
-- Gateway outbox and resume sessions added with the real transactional sync in Phase 1.
-- Presence, typing and voice state intentionally absent: Redis/ephemeral state only.
