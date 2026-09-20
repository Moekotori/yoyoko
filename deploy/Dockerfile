FROM rust:1.98-bookworm AS builder
WORKDIR /workspace
COPY Cargo.toml Cargo.lock ./
COPY src/server ./src/server
COPY database ./database
COPY docs/protocol ./docs/protocol
RUN cargo build --locked --release -p chat-server

FROM debian:bookworm-slim
RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=builder /workspace/target/release/chat-server /usr/local/bin/chat-server
COPY config.toml /app/config.toml
USER 65532:65532
EXPOSE 8080
ENTRYPOINT ["chat-server"]
