-- 1:1 DMs reuse channels/messages. kind=dm rows have no server.
ALTER TABLE channels DROP CONSTRAINT channels_kind_check;
ALTER TABLE channels ADD CONSTRAINT channels_kind_check CHECK (kind IN ('text', 'voice', 'dm'));
ALTER TABLE channels ALTER COLUMN server_id DROP NOT NULL;
ALTER TABLE channels ADD CONSTRAINT channels_dm_server CHECK (
    (kind = 'dm' AND server_id IS NULL) OR (kind <> 'dm' AND server_id IS NOT NULL)
);

CREATE TABLE dm_pairs (
    channel_id UUID PRIMARY KEY REFERENCES channels(id) ON DELETE CASCADE,
    user_low UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    user_high UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    UNIQUE (user_low, user_high),
    CHECK (user_low < user_high)
);
CREATE INDEX dm_pairs_user_low ON dm_pairs(user_low);
CREATE INDEX dm_pairs_user_high ON dm_pairs(user_high);
