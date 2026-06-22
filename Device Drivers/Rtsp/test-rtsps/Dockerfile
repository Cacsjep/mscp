# RTSPS test server for the MSC RTSP Driver.
# MediaMTX serving a generated H.264 + AAC test pattern over both plain RTSP
# and RTSPS (RTSP over TLS) with a self-signed certificate.

# Stage 1: generate a self-signed certificate for RTSPS.
# Done in a separate alpine stage so the final image needs no openssl.
FROM alpine:3.20 AS certs
RUN apk add --no-cache openssl && \
    openssl req -x509 -newkey rsa:2048 -nodes \
        -keyout /server.key -out /server.crt \
        -days 3650 \
        -subj "/CN=mscp-rtsps-test" \
        -addext "subjectAltName=DNS:localhost,IP:127.0.0.1"

# Stage 2: MediaMTX with bundled ffmpeg (needed for the test-pattern generator).
FROM bluenviron/mediamtx:latest-ffmpeg
COPY --from=certs /server.crt /server.key /
COPY mediamtx.yml /mediamtx.yml
