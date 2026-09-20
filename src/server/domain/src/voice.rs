use uuid::Uuid;

#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord)]
pub enum AudioQuality {
    Standard,
    High,
    VeryHigh,
    Studio,
}

impl AudioQuality {
    pub const DEFAULT: Self = Self::Studio;

    pub fn parse(value: &str) -> Option<Self> {
        match value {
            "standard" => Some(Self::Standard),
            "high" => Some(Self::High),
            "very_high" => Some(Self::VeryHigh),
            "studio" => Some(Self::Studio),
            _ => None,
        }
    }

    pub fn as_str(self) -> &'static str {
        match self {
            Self::Standard => "standard",
            Self::High => "high",
            Self::VeryHigh => "very_high",
            Self::Studio => "studio",
        }
    }

    pub fn bitrate_bps(self) -> u32 {
        match self {
            Self::Standard => 64_000,
            Self::High => 128_000,
            Self::VeryHigh => 256_000,
            Self::Studio => 510_000,
        }
    }

    pub fn channels(self) -> u8 {
        match self {
            Self::Standard => 1,
            _ => 2,
        }
    }

    pub fn sample_rate_hz(self) -> u32 {
        48_000
    }

    pub fn frame_ms(self) -> u32 {
        20
    }

    pub fn dtx(self) -> bool {
        matches!(self, Self::Standard)
    }

    pub fn fec(self) -> bool {
        true
    }

    pub fn clamp(self, max: Self) -> Self {
        self.min(max)
    }
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct VoiceState {
    pub user_id: Uuid,
    pub server_id: Uuid,
    pub channel_id: Uuid,
    pub self_mute: bool,
    pub self_deaf: bool,
    pub display_name: String,
    pub audio_quality: AudioQuality,
}

impl VoiceState {
    pub fn room_name(channel_id: Uuid) -> String {
        format!("voice:{channel_id}")
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn studio_is_opus_maximum_and_clamps() {
        assert_eq!(AudioQuality::Studio.bitrate_bps(), 510_000);
        assert_eq!(AudioQuality::Studio.channels(), 2);
        assert_eq!(
            AudioQuality::Studio.clamp(AudioQuality::High),
            AudioQuality::High
        );
        assert_eq!(
            AudioQuality::Standard.clamp(AudioQuality::Studio),
            AudioQuality::Standard
        );
        assert!(AudioQuality::parse("nope").is_none());
    }
}
