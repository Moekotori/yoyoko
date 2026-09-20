use crate::error::ApiErr;
use axum::http::StatusCode;
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
        max_bytes: u64,
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
            if size > max_bytes {
                let _ = tokio::fs::remove_file(tmp).await;
                let mib = (max_bytes / (1024 * 1024)).max(1);
                return Err(ApiErr::new(
                    StatusCode::PAYLOAD_TOO_LARGE,
                    "too_large",
                    format!("File exceeds {mib} MiB."),
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
            .filter(|ok| *ok)
            .map(|_| ())
    }
}

pub struct Sniffed {
    pub mime: &'static str,
    pub raster: bool,
}

pub fn sniff_file(path: &Path, mime: &str, file_name: &str) -> Result<Sniffed, ApiErr> {
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
    if let Some(kind) = raster_from_header(&header, read) {
        let mime_ok = mime.eq_ignore_ascii_case(kind)
            || mime.eq_ignore_ascii_case("application/octet-stream")
            || (kind == "image/jpeg" && mime.eq_ignore_ascii_case("image/jpg"));
        let ext_ok = match kind {
            "image/jpeg" => matches!(ext.as_str(), "jpg" | "jpeg" | ""),
            "image/png" => matches!(ext.as_str(), "png" | ""),
            "image/gif" => matches!(ext.as_str(), "gif" | ""),
            "image/webp" => matches!(ext.as_str(), "webp" | ""),
            _ => false,
        };
        if !mime_ok || !ext_ok {
            return Err(ApiErr::bad(
                "invalid_image",
                "Image type, MIME type, and extension must match.",
            ));
        }
        return Ok(Sniffed {
            mime: kind,
            raster: true,
        });
    }
    if matches!(ext.as_str(), "jpg" | "jpeg" | "png" | "gif" | "webp") {
        return Err(ApiErr::bad(
            "invalid_image",
            "Image contents do not match the file extension.",
        ));
    }
    let mapped = mime_for_ext(&ext).unwrap_or("application/octet-stream");
    let mime_ok = mime.is_empty()
        || mime.eq_ignore_ascii_case(mapped)
        || mime.eq_ignore_ascii_case("application/octet-stream");
    if !mime_ok {
        return Err(ApiErr::bad(
            "invalid_file",
            "MIME type and extension must match.",
        ));
    }
    Ok(Sniffed {
        mime: mapped,
        raster: false,
    })
}

fn raster_from_header(header: &[u8], read: usize) -> Option<&'static str> {
    if header.starts_with(JPEG) {
        Some("image/jpeg")
    } else if header.starts_with(PNG) {
        Some("image/png")
    } else if read >= 6 && header.starts_with(GIF) {
        Some("image/gif")
    } else if read >= 12 && header.starts_with(RIFF) && header[8..12] == *WEBP {
        Some("image/webp")
    } else {
        None
    }
}

fn mime_for_ext(ext: &str) -> Option<&'static str> {
    Some(match ext {
        "pdf" => "application/pdf",
        "txt" | "log" => "text/plain",
        "md" => "text/markdown",
        "csv" => "text/csv",
        "json" => "application/json",
        "xml" => "application/xml",
        "zip" => "application/zip",
        "gz" => "application/gzip",
        "tar" => "application/x-tar",
        "7z" => "application/x-7z-compressed",
        "mp3" => "audio/mpeg",
        "wav" => "audio/wav",
        "ogg" => "audio/ogg",
        "m4a" => "audio/mp4",
        "flac" => "audio/flac",
        "mp4" => "video/mp4",
        "webm" => "video/webm",
        "mkv" => "video/x-matroska",
        "mov" => "video/quicktime",
        "doc" => "application/msword",
        "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "xls" => "application/vnd.ms-excel",
        "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "ppt" => "application/vnd.ms-powerpoint",
        "pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "odt" => "application/vnd.oasis.opendocument.text",
        "rtf" => "application/rtf",
        "svg" => "image/svg+xml",
        _ => return None,
    })
}

fn generate_thumb(source: &Path, dest: &Path) -> bool {
    let Ok(reader) = image::ImageReader::open(source) else {
        return false;
    };
    let Ok(mut reader) = reader.with_guessed_format() else {
        return false;
    };
    let mut limits = image::Limits::default();
    limits.max_image_width = Some(8_192);
    limits.max_image_height = Some(8_192);
    limits.max_alloc = Some(64 * 1024 * 1024);
    reader.limits(limits);
    let Ok(image) = reader.decode() else {
        return false;
    };
    image.thumbnail(256, 256).to_rgb8().save(dest).is_ok()
}

pub fn is_animated(path: &Path, mime: &str) -> bool {
    match mime {
        "image/gif" => gif_is_animated(path),
        "image/webp" => webp_is_animated(path),
        "image/png" => png_is_animated(path),
        _ => false,
    }
}

fn gif_is_animated(path: &Path) -> bool {
    let Ok(file) = std::fs::File::open(path) else {
        return false;
    };
    let Ok(decoder) = image::codecs::gif::GifDecoder::new(std::io::BufReader::new(file)) else {
        return false;
    };
    use image::AnimationDecoder;
    decoder.into_frames().nth(1).is_some()
}

fn webp_is_animated(path: &Path) -> bool {
    let Ok(bytes) = std::fs::read(path) else {
        return false;
    };
    if bytes.len() >= 21
        && bytes.starts_with(b"RIFF")
        && bytes[8..12] == *WEBP
        && &bytes[12..16] == b"VP8X"
    {
        return bytes[20] & 0x02 != 0;
    }
    bytes.windows(4).any(|window| window == b"ANIM")
}

fn png_is_animated(path: &Path) -> bool {
    let Ok(bytes) = std::fs::read(path) else {
        return false;
    };
    if bytes.len() < 33 || !bytes.starts_with(&[0x89, b'P', b'N', b'G', 0x0D, 0x0A, 0x1A, 0x0A]) {
        return false;
    }
    let mut i = 8usize;
    while i + 8 <= bytes.len() {
        let len = u32::from_be_bytes(bytes[i..i + 4].try_into().unwrap()) as usize;
        let kind = &bytes[i + 4..i + 8];
        if kind == b"acTL" {
            return true;
        }
        if kind == b"IDAT" {
            return false;
        }
        i = i.saturating_add(12).saturating_add(len);
    }
    false
}

pub fn display_name(name: &str) -> String {
    Path::new(name)
        .file_name()
        .and_then(|n| n.to_str())
        .unwrap_or("file")
        .chars()
        .take(255)
        .collect()
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn png_thumbnail_writes_jpeg() {
        let dir = std::env::temp_dir().join(format!("thumb-{}", Uuid::now_v7()));
        std::fs::create_dir_all(&dir).unwrap();
        let src = dir.join("in");
        let dest = dir.join("out.jpg");
        std::fs::write(
            &src,
            [
                0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48,
                0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x02, 0x00, 0x00,
                0x00, 0x90, 0x77, 0x53, 0xDE, 0x00, 0x00, 0x00, 0x0C, 0x49, 0x44, 0x41, 0x54, 0x08,
                0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0x00, 0x00, 0x03, 0x01, 0x01, 0x00, 0x18, 0xDD, 0x8D,
                0xB0, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
            ],
        )
        .unwrap();
        assert!(generate_thumb(&src, &dest));
        assert!(dest.is_file());
        let _ = std::fs::remove_dir_all(dir);
    }
}
