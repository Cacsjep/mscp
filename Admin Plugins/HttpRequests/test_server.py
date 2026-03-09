"""
HTTP Requests Test Server
Spawns multiple endpoints simultaneously for testing all auth modes and HTTP/HTTPS.

Endpoints:
  http://localhost:4474   - No auth (accepts everything)
  http://localhost:4475   - Basic auth (admin:secret)
  http://localhost:4476   - Bearer auth (my-bearer-token-123)
  http://localhost:4477   - Digest auth (admin:secret)
  https://localhost:4478  - HTTPS no auth (self-signed cert)
  https://localhost:4479  - HTTPS + Basic auth
"""

from http.server import HTTPServer, BaseHTTPRequestHandler
from urllib.parse import urlparse, parse_qs
import json
import base64
import ssl
import os
import sys
import tempfile
import threading
from datetime import datetime

COLORS = {
    "reset": "\033[0m",
    "bold": "\033[1m",
    "dim": "\033[2m",
    "green": "\033[32m",
    "yellow": "\033[33m",
    "blue": "\033[34m",
    "magenta": "\033[35m",
    "cyan": "\033[36m",
    "red": "\033[31m",
    "gray": "\033[90m",
    "white": "\033[97m",
}

# Auth config
AUTH_USER = "admin"
AUTH_PASS = "secret"
AUTH_TOKEN = "my-bearer-token-123"
DIGEST_NONCE = "test-nonce-123456"

# Print lock for thread safety
_print_lock = threading.Lock()


def c(color, text):
    return f"{COLORS.get(color, '')}{text}{COLORS['reset']}"


def safe_print(*args, **kwargs):
    with _print_lock:
        print(*args, **kwargs)


def print_separator():
    safe_print(c("dim", "-" * 70))


def print_headers(headers):
    safe_print(c("cyan", "  Headers:"))
    for key, val in headers.items():
        display = val
        if key.lower() == "authorization":
            display = c("yellow", val)
        safe_print(f"    {c('gray', key)}: {display}")


def print_query_params(path):
    parsed = urlparse(path)
    params = parse_qs(parsed.query)
    if params:
        safe_print(c("cyan", "  Query Parameters:"))
        for key, vals in params.items():
            for v in vals:
                safe_print(f"    {c('magenta', key)} = {v}")


def print_body(body_bytes):
    if not body_bytes:
        safe_print(c("gray", "  (no body)"))
        return
    try:
        data = json.loads(body_bytes)
        safe_print(c("cyan", "  Body (JSON):"))
        formatted = json.dumps(data, indent=2)
        for line in formatted.split("\n"):
            safe_print(f"    {line}")
    except json.JSONDecodeError:
        text = body_bytes.decode("utf-8", errors="replace")
        safe_print(c("cyan", f"  Body ({len(body_bytes)} bytes):"))
        for line in text.split("\n")[:20]:
            safe_print(f"    {line}")


def check_basic_auth(handler):
    auth_header = handler.headers.get("Authorization", "")
    expected = base64.b64encode(f"{AUTH_USER}:{AUTH_PASS}".encode()).decode()
    if auth_header == f"Basic {expected}":
        safe_print(c("green", f"  Auth: Basic OK (user={AUTH_USER})"))
        return True
    safe_print(c("red", f"  Auth: Basic FAILED - got: {auth_header or '(none)'}"))
    handler.send_response(401)
    handler.send_header("WWW-Authenticate", 'Basic realm="Test Server"')
    handler.send_header("Content-Type", "application/json")
    handler.end_headers()
    handler.wfile.write(json.dumps({"error": "Unauthorized", "expected": "Basic", "user": AUTH_USER, "pass": AUTH_PASS}).encode())
    return False


def check_bearer_auth(handler):
    auth_header = handler.headers.get("Authorization", "")
    if auth_header == f"Bearer {AUTH_TOKEN}":
        safe_print(c("green", "  Auth: Bearer OK"))
        return True
    safe_print(c("red", f"  Auth: Bearer FAILED - got: {auth_header or '(none)'}"))
    handler.send_response(401)
    handler.send_header("WWW-Authenticate", 'Bearer realm="Test Server"')
    handler.send_header("Content-Type", "application/json")
    handler.end_headers()
    handler.wfile.write(json.dumps({"error": "Unauthorized", "expected": "Bearer", "token": AUTH_TOKEN}).encode())
    return False


def check_digest_auth(handler):
    auth_header = handler.headers.get("Authorization", "")
    if not auth_header.startswith("Digest "):
        safe_print(c("yellow", "  Auth: Digest challenge sent"))
        handler.send_response(401)
        handler.send_header(
            "WWW-Authenticate",
            f'Digest realm="Test Server", nonce="{DIGEST_NONCE}", qop="auth", algorithm=MD5',
        )
        handler.send_header("Content-Type", "application/json")
        handler.end_headers()
        handler.wfile.write(json.dumps({"error": "Digest challenge"}).encode())
        return False
    if f'username="{AUTH_USER}"' in auth_header:
        safe_print(c("green", f"  Auth: Digest OK (user={AUTH_USER})"))
        return True
    safe_print(c("red", f"  Auth: Digest FAILED - got: {auth_header}"))
    handler.send_response(401)
    handler.send_header("Content-Type", "application/json")
    handler.end_headers()
    handler.wfile.write(json.dumps({"error": "Unauthorized"}).encode())
    return False


def make_handler(auth_check, label):
    """Create a handler class with the given auth check function."""

    class Handler(BaseHTTPRequestHandler):
        def _handle(self, method):
            now = datetime.now().strftime("%H:%M:%S.%f")[:-3]
            path = self.path
            parsed = urlparse(path)

            safe_print()
            print_separator()
            safe_print(
                f"  {c('bold', now)}  "
                f"{c('white', f'[{label}]')}  "
                f"{c('green', method)}  "
                f"{c('blue', parsed.path)}"
                f"{c('gray', '?' + parsed.query if parsed.query else '')}"
            )
            print_separator()

            print_headers(self.headers)
            print_query_params(path)

            if auth_check and not auth_check(self):
                print_separator()
                return

            length = int(self.headers.get("Content-Length", 0))
            body = self.rfile.read(length) if length > 0 else b""
            print_body(body)

            print_separator()

            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.end_headers()
            response = {
                "status": "ok",
                "server": label,
                "method": method,
                "path": parsed.path,
                "timestamp": now,
            }
            if parsed.query:
                response["query"] = dict(parse_qs(parsed.query))
            self.wfile.write(json.dumps(response, indent=2).encode())

        def do_GET(self):
            self._handle("GET")

        def do_POST(self):
            self._handle("POST")

        def do_PUT(self):
            self._handle("PUT")

        def do_DELETE(self):
            self._handle("DELETE")

        def do_PATCH(self):
            self._handle("PATCH")

        def log_message(self, format, *args):
            pass

    return Handler


def generate_self_signed_cert():
    """Generate a self-signed cert for HTTPS testing."""
    try:
        from cryptography import x509
        from cryptography.x509.oid import NameOID
        from cryptography.hazmat.primitives import hashes, serialization
        from cryptography.hazmat.primitives.asymmetric import rsa
        import datetime as dt

        key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
        subject = issuer = x509.Name([
            x509.NameAttribute(NameOID.COMMON_NAME, "localhost"),
        ])
        cert = (
            x509.CertificateBuilder()
            .subject_name(subject)
            .issuer_name(issuer)
            .public_key(key.public_key())
            .serial_number(x509.random_serial_number())
            .not_valid_before(dt.datetime.utcnow())
            .not_valid_after(dt.datetime.utcnow() + dt.timedelta(days=365))
            .add_extension(
                x509.SubjectAlternativeName([
                    x509.DNSName("localhost"),
                    x509.IPAddress(ipaddress.IPv4Address("127.0.0.1")),
                ]),
                critical=False,
            )
            .sign(key, hashes.SHA256())
        )

        tmpdir = tempfile.mkdtemp()
        cert_path = os.path.join(tmpdir, "cert.pem")
        key_path = os.path.join(tmpdir, "key.pem")

        with open(cert_path, "wb") as f:
            f.write(cert.public_bytes(serialization.Encoding.PEM))
        with open(key_path, "wb") as f:
            f.write(key.private_bytes(
                serialization.Encoding.PEM,
                serialization.PrivateFormat.TraditionalOpenSSL,
                serialization.NoEncryption(),
            ))
        return cert_path, key_path
    except ImportError:
        return None, None


def start_server(port, handler_class, label, use_ssl=False, cert_path=None, key_path=None):
    """Start an HTTP(S) server in a background thread."""
    server = HTTPServer(("0.0.0.0", port), handler_class)
    if use_ssl and cert_path and key_path:
        ctx = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
        ctx.load_cert_chain(cert_path, key_path)
        server.socket = ctx.wrap_socket(server.socket, server_side=True)
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    return server


if __name__ == "__main__":
    import ipaddress

    safe_print(c("bold", "\n  HTTP Requests Test Server"))
    safe_print(c("dim", "  ========================\n"))

    servers = []

    # HTTP servers
    endpoints = [
        (4474, None,              "No Auth"),
        (4475, check_basic_auth,  "Basic Auth"),
        (4476, check_bearer_auth, "Bearer Auth"),
        (4477, check_digest_auth, "Digest Auth"),
    ]

    for port, auth_fn, label in endpoints:
        handler = make_handler(auth_fn, label)
        srv = start_server(port, handler, label)
        servers.append(srv)
        scheme = "http"
        url = f"{scheme}://localhost:{port}"
        auth_info = ""
        if "Basic" in label or "Digest" in label:
            auth_info = f"  ({AUTH_USER}:{AUTH_PASS})"
        elif "Bearer" in label:
            auth_info = f"  (token: {AUTH_TOKEN})"
        safe_print(f"  {c('green', 'OK')}  {c('blue', url):<45} {c('white', label)}{c('gray', auth_info)}")

    # HTTPS servers (if cryptography is available)
    cert_path, key_path = generate_self_signed_cert()
    if cert_path:
        https_endpoints = [
            (4478, None,             "HTTPS No Auth"),
            (4479, check_basic_auth, "HTTPS + Basic"),
        ]
        for port, auth_fn, label in https_endpoints:
            handler = make_handler(auth_fn, label)
            srv = start_server(port, handler, label, use_ssl=True, cert_path=cert_path, key_path=key_path)
            servers.append(srv)
            url = f"https://localhost:{port}"
            auth_info = ""
            if "Basic" in label:
                auth_info = f"  ({AUTH_USER}:{AUTH_PASS})"
            safe_print(f"  {c('green', 'OK')}  {c('blue', url):<45} {c('white', label)}{c('gray', auth_info)}")
    else:
        safe_print()
        safe_print(c("yellow", "  HTTPS disabled - install 'cryptography' package for HTTPS:"))
        safe_print(c("gray",   "    pip install cryptography"))

    safe_print()
    safe_print(c("dim", "  Credentials:"))
    safe_print(f"    Username: {c('cyan', AUTH_USER)}")
    safe_print(f"    Password: {c('cyan', AUTH_PASS)}")
    safe_print(f"    Token:    {c('cyan', AUTH_TOKEN)}")
    safe_print()
    safe_print(c("dim", "  Press Ctrl+C to stop all servers"))
    safe_print()

    try:
        threading.Event().wait()
    except KeyboardInterrupt:
        safe_print(c("yellow", "\n  Shutting down..."))
        for srv in servers:
            srv.shutdown()
        safe_print(c("green", "  Done.\n"))
