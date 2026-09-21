-- Hot-path indexes for membership lookup, READY channel lists, and cooldown checks.
CREATE INDEX IF NOT EXISTS members_user ON members(user_id);
CREATE INDEX IF NOT EXISTS messages_author_recent
    ON messages(channel_id, author_id, id DESC)
    WHERE deleted_at IS NULL;
