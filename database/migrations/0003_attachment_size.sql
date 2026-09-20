-- Configurable upload limit is enforced in the service. Keep a hard ceiling in the table.
ALTER TABLE attachments DROP CONSTRAINT IF EXISTS attachments_size_bytes_check;
ALTER TABLE attachments ADD CONSTRAINT attachments_size_bytes_check
    CHECK (size_bytes > 0 AND size_bytes <= 268435456);
