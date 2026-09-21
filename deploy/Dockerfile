# syntax=docker/dockerfile:1
FROM rust:1.98-bookworm AS builder
WORKDIR /workspace
COPY Cargo.toml Cargo.lock ./
COPY src/server ./src/server
# Cargo resolves every workspace member even with -p chat-server. Only its
# manifest and empty target paths are needed; no desktop/media code is built.
COPY src/media-worker/Cargo.toml ./src/media-worker/Cargo.toml
RUN mkdir -p src/media-worker/src && touch src/media-worker/src/main.rs src/media-worker/src/lib.rs
COPY database ./database
COPY docs/protocol ./docs/protocol
RUN --mount=type=cache,target=/usr/local/cargo/registry \
    --mount=type=cache,target=/usr/local/cargo/git \
    --mount=type=cache,target=/workspace/target \
    cargo build --locked --release -p chat-server \
    && cp /workspace/target/release/chat-server /tmp/chat-server

FROM debian:bookworm-slim
RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=builder /tmp/chat-server /usr/local/bin/chat-server
COPY config.toml /app/config.toml
RUN mkdir -p /app/data/objects && chown -R 65532:65532 /app/data
USER 65532:65532
EXPOSE 8080
ENTRYPOINT ["chat-server"]
