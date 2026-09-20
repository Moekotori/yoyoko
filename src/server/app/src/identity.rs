use chrono::Utc;
use hmac::{Hmac, Mac};
use rand::{RngCore, rngs::OsRng};
use sha2::{Digest, Sha256};
use uuid::Uuid;

type HmacSha256 = Hmac<Sha256>;

#[derive(Clone)]
pub struct TokenService {
    secret: Vec<u8>,
    pub access_ttl_seconds: u64,
    pub refresh_ttl_days: u64,
}

#[derive(Debug, Clone, Copy)]
pub struct AccessClaims {
    pub user_id: Uuid,
    pub session_id: Uuid,
}

impl TokenService {
    pub fn new(secret: &str, access_ttl_seconds: u64, refresh_ttl_days: u64) -> Self {
        Self {
            secret: secret.as_bytes().to_vec(),
            access_ttl_seconds,
            refresh_ttl_days,
        }
    }

    pub fn issue_access(&self, user_id: Uuid, session_id: Uuid) -> String {
        let exp = Utc::now().timestamp() + self.access_ttl_seconds as i64;
        let payload = format!("v1.{user_id}.{session_id}.{exp}");
        let mac = sign(&self.secret, payload.as_bytes());
        format!("{payload}.{}", hex::encode(mac))
    }

    pub fn verify_access(&self, token: &str) -> Result<AccessClaims, ()> {
        let (payload, mac_hex) = token.rsplit_once('.').ok_or(())?;
        let expected = sign(&self.secret, payload.as_bytes());
        let given = hex::decode(mac_hex).map_err(|_| ())?;
        if given.len() != expected.len()
            || given
                .iter()
                .zip(expected.iter())
                .fold(0u8, |acc, (a, b)| acc | (a ^ b))
                != 0
        {
            return Err(());
        }
        let mut parts = payload.split('.');
        if parts.next() != Some("v1") {
            return Err(());
        }
        let user_id = parts.next().and_then(|v| v.parse().ok()).ok_or(())?;
        let session_id = parts.next().and_then(|v| v.parse().ok()).ok_or(())?;
        let exp: i64 = parts.next().and_then(|v| v.parse().ok()).ok_or(())?;
        if parts.next().is_some() || exp < Utc::now().timestamp() {
            return Err(());
        }
        Ok(AccessClaims {
            user_id,
            session_id,
        })
    }

    pub fn random_refresh() -> (String, [u8; 32]) {
        let mut bytes = [0u8; 32];
        OsRng.fill_bytes(&mut bytes);
        let token = hex::encode(bytes);
        let hash = sha_refresh(&token);
        (token, hash)
    }

    pub fn hash_refresh(token: &str) -> [u8; 32] {
        sha_refresh(token)
    }

    pub fn sign_attachment(&self, id: Uuid, exp: i64, kind: &str) -> String {
        let payload = format!("{id}:{exp}:{kind}");
        hex::encode(sign(&self.secret, payload.as_bytes()))
    }

    pub fn verify_attachment(&self, id: Uuid, exp: i64, kind: &str, sig: &str) -> bool {
        if exp < Utc::now().timestamp() {
            return false;
        }
        let expected = self.sign_attachment(id, exp, kind);
        expected.len() == sig.len()
            && expected
                .bytes()
                .zip(sig.bytes())
                .fold(0u8, |acc, (a, b)| acc | (a ^ b))
                == 0
    }
}

fn sign(secret: &[u8], payload: &[u8]) -> Vec<u8> {
    let mut mac = HmacSha256::new_from_slice(secret).expect("hmac key");
    mac.update(payload);
    mac.finalize().into_bytes().to_vec()
}

fn sha_refresh(token: &str) -> [u8; 32] {
    Sha256::digest(token.as_bytes()).into()
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn access_token_roundtrip() {
        let tokens = TokenService::new("local-development-only-change-me-32b", 3600, 30);
        let user = Uuid::now_v7();
        let session = Uuid::now_v7();
        let token = tokens.issue_access(user, session);
        let claims = tokens.verify_access(&token).unwrap();
        assert_eq!(claims.user_id, user);
        assert_eq!(claims.session_id, session);
        assert!(tokens.verify_access("v1.bad").is_err());
    }
}
