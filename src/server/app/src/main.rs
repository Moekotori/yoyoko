use chat_server::{
    api, configuration::Settings, database, objects::ObjectStore, services, state::AppState,
};
use tracing_subscriber::EnvFilter;

#[tokio::main]
async fn main() -> Result<(), Box<dyn std::error::Error>> {
    let settings =
        Settings::load(&std::env::var("CHAT_CONFIG").unwrap_or_else(|_| "config.toml".into()))?;
    let (writer, _guard) = tracing_appender::non_blocking::NonBlockingBuilder::default()
        .buffered_lines_limit(1024)
        .lossy(true)
        .finish(std::io::stdout());
    tracing_subscriber::fmt()
        .with_writer(writer)
        .with_env_filter(EnvFilter::try_new(&settings.logging.filter)?)
        .init();
    let args: Vec<String> = std::env::args().skip(1).collect();
    match args.first().map(String::as_str) {
        Some("migrate") => {
            database::migrate(&settings.database).await?;
            return Ok(());
        }
        Some("check-config") => {
            println!("Configuration valid (secrets omitted)");
            return Ok(());
        }
        Some(_) => return Err("Usage: chat-server [migrate|check-config]".into()),
        None => (),
    }
    let store = database::open(&settings.database).await?;
    let objects = ObjectStore::open(&settings.storage.local_dir)?;
    tokio::spawn(services::trim_loop(store.clone()));
    let state = AppState::new(settings.clone(), store, objects);
    let listener = tokio::net::TcpListener::bind(settings.server.listen).await?;
    tracing::info!(
        address = %listener.local_addr()?,
        public_url = %settings.server.public_url,
        "API listening"
    );
    axum::serve(
        listener,
        api::router(state).into_make_service_with_connect_info::<std::net::SocketAddr>(),
    )
    .with_graceful_shutdown(shutdown())
    .await?;
    Ok(())
}

async fn shutdown() {
    #[cfg(unix)]
    {
        let mut terminate =
            tokio::signal::unix::signal(tokio::signal::unix::SignalKind::terminate())
                .expect("SIGTERM handler");
        tokio::select! { _ = tokio::signal::ctrl_c() => {}, _ = terminate.recv() => {} }
    }
    #[cfg(not(unix))]
    let _ = tokio::signal::ctrl_c().await;
}
