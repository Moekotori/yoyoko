-- Voice channel audio quality cap. Closed set; studio is Opus maximum (510 kbps).
ALTER TABLE channels
    ADD COLUMN audio_quality TEXT NOT NULL DEFAULT 'studio'
    CHECK (audio_quality IN ('standard', 'high', 'very_high', 'studio'));
