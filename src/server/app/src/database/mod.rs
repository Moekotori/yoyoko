pub mod channel_repository;

use crate::configuration::Database;
use sqlx::{PgPool, postgres::PgPoolOptions};

pub fn pool(settings: &Database) -> Result<PgPool, sqlx::Error> {
    PgPoolOptions::new()
        .max_connections(settings.max_connections)
        .min_connections(0)
        .acquire_timeout(std::time::Duration::from_secs(3))
        .connect_lazy(&settings.url)
}
pub async fn migrate(pool: &PgPool) -> Result<(), sqlx::migrate::MigrateError> {
    sqlx::migrate!("../../../database/migrations")
        .run(pool)
        .await
}
