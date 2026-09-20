use crate::error::ApiErr;
use axum::http::StatusCode;
use chat_protocol::MAX_ATTACHMENT_BYTES;
use std::path::{Path, PathBuf};
use tokio::io::AsyncWriteExt;
use uuid::Uuid;

const JPEG: &[u8] = &[0xFF, 0xD8, 0xFF];
const PNG: &[u8] = &[0x89, 0x50, 0x4E, 0x47];
const GIF: &[u8] = b"GIF8";
const RIFF: &[u8] = b"RIFF";
const WEBP: &[u8] = b"WEBP";

#[derive(Clone)]
pub struct ObjectStore {
    root: PathBuf,
}

impl ObjectStore {
    pub fn open(dir: &str) -> Result<Self, std::io::Error> {
        let root = PathBuf::from(dir);
        std::fs::create_dir_all(&root)?;
        Ok(Self { root })
    }

    pub fn path(&self, key: Uuid) -> PathBuf {
        self.root.join(key.to_string())
    }

    pub fn thumb_path(&self, key: Uuid) -> PathBuf {
        self.root.join(format!("{key}.thumb.jpg"))
    }

    pub fn tmp_path(&self) -> PathBuf {
        self.root.join(format!("{}.tmp", Uuid::now_v7()))
    }

    pub async fn write_limited(
        &self,
        tmp: &Path,
        mut field: axum::extract::multipart::Field<'_>,
    ) -> Result<u64, ApiErr> {
        let mut file = tokio::fs::File::create(tmp)
            .await
            .map_err(|_| ApiErr::unavailable())?;
        let mut size = 0u64;
        while let Some(chunk) = field
            .chunk()
            .await
            .map_err(|_| ApiErr::bad("invalid_upload", "Failed to read upload."))?
        {
            size += chunk.len() as u64;
            if size > MAX_ATTACHMENT_BYTES {
                let _ = tokio::fs::remove_file(tmp).await;
                return Err(ApiErr::new(
                    StatusCode::PAYLOAD_TOO_LARGE,
                    "too_large",
                    "Image exceeds 24 MiB.",
                ));
            }
            file.write_all(&chunk)
                .await
                .map_err(|_| ApiErr::unavailable())?;
        }
        file.flush().await.map_err(|_| ApiErr::unavailable())?;
        if size == 0 {
            let _ = tokio::fs::remove_file(tmp).await;
            return Err(ApiErr::bad("invalid_upload", "Empty file."));
        }
        Ok(size)
    }

    pub async fn commit(&self, tmp: &Path, key: Uuid) -> Result<(), ApiErr> {
        tokio::fs::rename(tmp, self.path(key))
            .await
            .map_err(|_| ApiErr::unavailable())
    }

    pub async fn thumbnail(&self, key: Uuid) -> Option<()> {
        let source = self.path(key);
        let dest = self.thumb_path(key);
        tokio::task::spawn_blocking(move || generate_thumb(&source, &dest))
            .await
            .ok()
            .and_then(|r| r.ok())
    }
}

pub fn sniff_image(path: &Path, mime: &str, file_name: &str) -> Result<&'static str, ApiErr> {
    let mut header = [0u8; 16];
    let read = std::fs::File::open(path)
        .and_then(|mut f| {
            use std::io::Read;
            f.read(&mut header)
        })
        .map_err(|_| ApiErr::unavailable())?;
    let ext = Path::new(file_name)
        .extension()
        .and_then(|e| e.to_str())
        .unwrap_or("")
        .to_ascii_lowercase();
    let kind = if header.starts_with(JPEG) {
        "image/jpeg"
    } else if header.starts_with(PNG) {
        "image/png"
    } else if read >= 6 && header.starts_with(GIF) {
        "image/gif"
    } else if read >= 12 && header.starts_with(RIFF) && header[8..12] == *WEBP {
        "image/webp"
    } else {
        return Err(ApiErr::bad(
            "invalid_image",
            "Only JPEG, PNG, GIF, or WebP images.",
        ));
    };
    let mime_ok = mime.eq_ignore_ascii_case(kind)
        || (kind == "image/jpeg" && mime.eq_ignore_ascii_case("image/jpg"));
    let ext_ok = match kind {
        "image/jpeg" => matches!(ext.as_str(), "jpg" | "jpeg"),
        "image/png" => ext == "png",
        "image/gif" => ext == "gif",
        "image/webp" => ext == "webp",
        _ => false,
    };
    if !mime_ok || !ext_ok {
        return Err(ApiErr::bad(
            "invalid_image",
            "File type, MIME type, and extension must match.",
        ));
    }
    Ok(kind)
}

fn generate_thumb(source: &Path, dest: &Path) -> Result<(), ()> {
    let image = image::open(source).map_err(|_| ())?;
    let thumb = image.thumbnail(256, 256);
    thumb
        .to_rgb8()
        .save_with_format(dest, image::ImageFormat::Jpeg)
        .map_err(|_| ())?;
    Ok(())
}

pub fn display_name(name: &str) -> String {
    Path::new(name)
        .file_name()
        .and_then(|n| n.to_str())
        .unwrap_or("image")
        .chars()
        .take(255)
        .collect()
}
