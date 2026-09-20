use crate::{configuration::Database, local::LocalStore, postgres::PgStore, store::Store};
use sqlx::{PgPool, postgres::PgPoolOptions};
use std::sync::Arc;

pub fn pool(settings: &Database) -> Result<PgPool, sqlx::Error> {
    PgPoolOptions::new()
        .max_connections(settings.max_connections)
        .min_connections(0)
        .acquire_timeout(std::time::Duration::from_secs(3))
        .connect_lazy(&settings.url)
}

pub async fn open(settings: &Database) -> Result<Arc<dyn Store>, Box<dyn std::error::Error>> {
    if settings.url.starts_with("local:") {
        Ok(Arc::new(LocalStore::open(&settings.url)?))
    } else {
        Ok(Arc::new(PgStore(pool(settings)?)))
    }
}

pub async fn migrate(settings: &Database) -> Result<(), Box<dyn std::error::Error>> {
    if settings.url.starts_with("local:") {
        LocalStore::open(&settings.url)?;
        return Ok(());
    }
    let pool = PgPoolOptions::new()
        .max_connections(1)
        .acquire_timeout(std::time::Duration::from_secs(8))
        .connect(&settings.url)
        .await?;
    sqlx::migrate!("../../../database/migrations")
        .run(&pool)
        .await?;
    pool.close().await;
    Ok(())
}
