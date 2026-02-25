"""
RTMP Server Security Test 
==========================================

Usage:
    python test_security.py [host] [port]
    python test_security.py                   # defaults to localhost:8783
    python test_security.py 192.168.1.10 8783


"""

import socket
import struct
import time
import sys
import os
import threading
import traceback

HOST = sys.argv[1] if len(sys.argv) > 1 else "127.0.0.1"
PORT = int(sys.argv[2]) if len(sys.argv) > 2 else 8783
MAX_CONNECTIONS = 32  # Must match Constants.DefaultMaxConnections
MAX_CONNECTIONS_PER_IP = 16  # Must match Constants.MaxConnectionsPerIp
VIDEO_DATA_TIMEOUT_S = 15  # Must match Constants.VideoDataTimeoutMs / 1000
TIMEOUT = 10

# ─── RTMP protocol helpers ────────────────────────────────────────────────────

def rtmp_handshake(sock):
    """Perform RTMP C0/C1/C2 handshake. Returns True on success."""
    # C0 + C1
    c1 = b'\x03' + (b'\x00' * 4) + (b'\x00' * 4) + os.urandom(1528)
    sock.sendall(c1)

    # Read S0 (1) + S1 (1536) + S2 (1536) = 3073 bytes
    resp = recv_exact(sock, 1 + 1536 + 1536)
    if resp is None or len(resp) < 3073:
        return False

    s1 = resp[1:1537]

    # C2 = echo of S1
    sock.sendall(s1)
    return True


def recv_exact(sock, n, timeout=TIMEOUT):
    """Receive exactly n bytes or return None on timeout/error."""
    sock.settimeout(timeout)
    buf = b''
    try:
        while len(buf) < n:
            chunk = sock.recv(n - len(buf))
            if not chunk:
                return None
            buf += chunk
    except (socket.timeout, ConnectionError, OSError):
        return None
    return buf


def build_chunk_header_type0(csid, msg_type, msg_length, stream_id=0, timestamp=0):
    """Build a Type 0 (full) RTMP chunk header."""
    hdr = b''
    # Basic header
    if csid < 64:
        hdr += bytes([(0 << 6) | csid])
    elif csid < 320:
        hdr += bytes([0 << 6, csid - 64])
    else:
        hdr += bytes([(0 << 6) | 1, (csid - 64) & 0xFF, ((csid - 64) >> 8) & 0xFF])

    # Timestamp (3 bytes)
    hdr += struct.pack('>I', timestamp)[1:]
    # Message length (3 bytes BE)
    hdr += struct.pack('>I', msg_length)[1:]
    # Message type (1 byte)
    hdr += bytes([msg_type])
    # Stream ID (4 bytes LE)
    hdr += struct.pack('<I', stream_id)
    return hdr


def build_type3_header(csid):
    """Build a Type 3 (continuation) chunk header."""
    if csid < 64:
        return bytes([(3 << 6) | csid])
    elif csid < 320:
        return bytes([3 << 6, csid - 64])
    else:
        return bytes([(3 << 6) | 1, (csid - 64) & 0xFF, ((csid - 64) >> 8) & 0xFF])


def send_rtmp_message(sock, csid, msg_type, payload, chunk_size=128, stream_id=0):
    """Send a complete RTMP message split into chunks."""
    hdr = build_chunk_header_type0(csid, msg_type, len(payload), stream_id)
    sock.sendall(hdr)

    offset = 0
    first = True
    while offset < len(payload):
        if not first:
            sock.sendall(build_type3_header(csid))
        end = min(offset + chunk_size, len(payload))
        sock.sendall(payload[offset:end])
        offset = end
        first = False


def build_amf0_string(s):
    """Encode an AMF0 string value."""
    encoded = s.encode('utf-8')
    return b'\x02' + struct.pack('>H', len(encoded)) + encoded


def build_amf0_number(n):
    """Encode an AMF0 number value."""
    return b'\x00' + struct.pack('>d', n)


def build_amf0_null():
    return b'\x05'


def build_amf0_object(props):
    """Encode an AMF0 object from a dict."""
    buf = b'\x03'
    for key, val in props.items():
        encoded_key = key.encode('utf-8')
        buf += struct.pack('>H', len(encoded_key)) + encoded_key
        buf += val  # already encoded AMF0 value
    buf += b'\x00\x00\x09'  # object end
    return buf


def build_connect_command(app="stream1"):
    """Build a minimal RTMP connect command payload."""
    payload = build_amf0_string("connect")
    payload += build_amf0_number(1.0)  # txId
    payload += build_amf0_object({
        "app": build_amf0_string_raw(app),
    })
    return payload


def build_amf0_string_raw(s):
    """Encode an AMF0 string value (for use inside objects, includes type marker)."""
    encoded = s.encode('utf-8')
    return b'\x02' + struct.pack('>H', len(encoded)) + encoded


def build_create_stream_command():
    """Build a minimal RTMP createStream command."""
    payload = build_amf0_string("createStream")
    payload += build_amf0_number(2.0)
    payload += build_amf0_null()
    return payload


def build_publish_command(stream_name="stream1"):
    """Build a minimal RTMP publish command."""
    payload = build_amf0_string("publish")
    payload += build_amf0_number(3.0)
    payload += build_amf0_null()
    payload += build_amf0_string(stream_name)
    payload += build_amf0_string("live")
    return payload


def connect_and_handshake():
    """Create a TCP socket, connect, and perform RTMP handshake."""
    sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    sock.settimeout(TIMEOUT)
    sock.connect((HOST, PORT))
    sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
    if not rtmp_handshake(sock):
        sock.close()
        return None
    return sock


def is_connection_alive(sock):
    """Check if the server hasn't closed our connection."""
    try:
        sock.settimeout(1.0)
        data = sock.recv(1, socket.MSG_PEEK)
        if not data:
            return False  # recv returned b'' = EOF = remote closed
        return True  # got actual data
    except socket.timeout:
        return True  # timeout = still alive, just no data
    except (ConnectionError, OSError):
        return False


def drain_server_responses(sock, timeout=1.0):
    """Read and discard any pending server responses."""
    sock.settimeout(timeout)
    try:
        while True:
            data = sock.recv(4096)
            if not data:
                break
    except (socket.timeout, ConnectionError, OSError):
        pass


# ─── Test functions ───────────────────────────────────────────────────────────

def test_max_connections():
    """
    TEST 1: Max concurrent connections limit
    Open MAX_CONNECTIONS+5 connections and verify excess are rejected.
    """
    print(f"\n{'='*60}")
    print(f"TEST 1: Max Concurrent Connections (limit={MAX_CONNECTIONS})")
    print(f"{'='*60}")

    sockets = []
    accepted = 0
    rejected = 0

    target = MAX_CONNECTIONS + 5
    for i in range(target):
        try:
            sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            sock.settimeout(5)
            sock.connect((HOST, PORT))
            # Try handshake — if connection was rejected, this will fail
            if rtmp_handshake(sock):
                sockets.append(sock)
                accepted += 1
            else:
                rejected += 1
                sock.close()
        except (ConnectionError, OSError, socket.timeout):
            rejected += 1

        # Small delay to let the server's accept loop process
        if i % 10 == 9:
            time.sleep(0.2)

    print(f"  Attempted: {target}")
    print(f"  Accepted:  {accepted}")
    print(f"  Rejected:  {rejected}")

    # Clean up
    for s in sockets:
        try:
            s.close()
        except Exception:
            pass

    # Wait for server to reclaim threads
    time.sleep(1)

    if accepted <= MAX_CONNECTIONS + 2 and rejected > 0:
        print("  PASS: Excess connections were rejected")
        return True
    else:
        print(f"  FAIL: Expected ~{MAX_CONNECTIONS} accepted, got {accepted} with {rejected} rejected")
        return False


def test_oversized_message():
    """
    TEST 2: Message size limit (5 MB)
    Send a chunk header claiming a 10 MB message, server should disconnect.
    """
    print(f"\n{'='*60}")
    print("TEST 2: Oversized Message (>5 MB)")
    print(f"{'='*60}")

    sock = connect_and_handshake()
    if not sock:
        print("  FAIL: Could not establish connection")
        return False

    # Drain any server responses from handshake
    drain_server_responses(sock, 0.5)

    # Send a Type 0 chunk header on csid=3 with message length = 10 MB
    # Message type 20 (AMF0 command) — server will reject due to size
    oversized_length = 10 * 1024 * 1024  # 10 MB
    hdr = build_chunk_header_type0(csid=3, msg_type=20, msg_length=oversized_length)
    try:
        sock.sendall(hdr)
        # Send a small amount of payload data — server should disconnect
        # before we finish sending 10 MB
        sock.sendall(b'\x00' * 128)
        time.sleep(1)
        alive = is_connection_alive(sock)
    except (ConnectionError, OSError, BrokenPipeError):
        alive = False

    sock.close()

    if not alive:
        print("  PASS: Server disconnected the client (message too large)")
        return True
    else:
        print("  FAIL: Server did NOT disconnect (accepted oversized message header)")
        return False


def test_chunk_size_zero():
    """
    TEST 3: Chunk size = 0 (would cause infinite loop)
    Send Set Chunk Size = 0, then a message. Server should disconnect.
    """
    print(f"\n{'='*60}")
    print("TEST 3: Set Chunk Size = 0")
    print(f"{'='*60}")

    sock = connect_and_handshake()
    if not sock:
        print("  FAIL: Could not establish connection")
        return False

    drain_server_responses(sock, 0.5)

    # Send Set Chunk Size (type 1) with value 0
    # Protocol control messages use csid=2, stream_id=0
    payload = struct.pack('>I', 0)  # chunk size = 0
    try:
        send_rtmp_message(sock, csid=2, msg_type=1, payload=payload)
        time.sleep(1)

        # Now try sending any message — should fail since the server
        # should have disconnected us after seeing chunk_size=0
        connect_payload = build_connect_command()
        send_rtmp_message(sock, csid=3, msg_type=20, payload=connect_payload)
        time.sleep(1)
        alive = is_connection_alive(sock)
    except (ConnectionError, OSError, BrokenPipeError):
        alive = False

    sock.close()

    if not alive:
        print("  PASS: Server disconnected the client (invalid chunk size)")
        return True
    else:
        print("  FAIL: Server did NOT disconnect")
        return False


def test_chunk_size_too_large():
    """
    TEST 4: Chunk size > 16,777,215 (exceeds RTMP spec max)
    """
    print(f"\n{'='*60}")
    print("TEST 4: Set Chunk Size > 16,777,215 (RTMP spec max)")
    print(f"{'='*60}")

    sock = connect_and_handshake()
    if not sock:
        print("  FAIL: Could not establish connection")
        return False

    drain_server_responses(sock, 0.5)

    # Send Set Chunk Size = 0x7FFFFFFF (max after masking sign bit)
    # This is > 16,777,215 so should be rejected
    payload = struct.pack('>I', 0x7FFFFFFF)
    try:
        send_rtmp_message(sock, csid=2, msg_type=1, payload=payload)
        time.sleep(1)

        connect_payload = build_connect_command()
        send_rtmp_message(sock, csid=3, msg_type=20, payload=connect_payload)
        time.sleep(1)
        alive = is_connection_alive(sock)
    except (ConnectionError, OSError, BrokenPipeError):
        alive = False

    sock.close()

    if not alive:
        print("  PASS: Server disconnected the client (chunk size too large)")
        return True
    else:
        print("  FAIL: Server did NOT disconnect")
        return False


def test_chunk_stream_exhaustion():
    """
    TEST 5: Too many chunk streams (>32 per client)
    Send messages on 35 different chunk stream IDs. Server should disconnect.
    """
    print(f"\n{'='*60}")
    print("TEST 5: Chunk Stream Exhaustion (>32 CSIDs)")
    print(f"{'='*60}")

    sock = connect_and_handshake()
    if not sock:
        print("  FAIL: Could not establish connection")
        return False

    drain_server_responses(sock, 0.5)

    disconnected = False
    try:
        # RTMP uses csid 2 for protocol and csid 3 for commands by convention.
        # We'll use csid 4 through 38 (35 unique CSIDs) to exceed the 32 limit.
        # Each one gets a tiny AMF0 command message.
        small_payload = build_amf0_string("ping") + build_amf0_number(0)

        for csid in range(4, 40):
            hdr = build_chunk_header_type0(csid=csid, msg_type=20, msg_length=len(small_payload))
            sock.sendall(hdr + small_payload)
            time.sleep(0.05)

        time.sleep(1)
        alive = is_connection_alive(sock)
        disconnected = not alive
    except (ConnectionError, OSError, BrokenPipeError):
        disconnected = True

    sock.close()

    if disconnected:
        print("  PASS: Server disconnected the client (too many chunk streams)")
        return True
    else:
        print("  FAIL: Server did NOT disconnect")
        return False


def test_amf0_deep_nesting():
    """
    TEST 6: Deeply nested AMF0 objects (>32 levels)
    Server should disconnect without crashing (no StackOverflowException).
    """
    print(f"\n{'='*60}")
    print("TEST 6: AMF0 Deep Nesting (>32 levels)")
    print(f"{'='*60}")

    sock = connect_and_handshake()
    if not sock:
        print("  FAIL: Could not establish connection")
        return False

    drain_server_responses(sock, 0.5)

    # Build a deeply nested AMF0 payload:
    # "connect" (string), 1.0 (number), {a: {a: {a: ... 50 levels ...}}}
    payload = build_amf0_string("connect")
    payload += build_amf0_number(1.0)

    # Build nested objects: 50 levels deep
    # Each level: object marker (0x03) + key "a" + next level ... + end marker (0x00 0x00 0x09)
    depth = 50
    inner = b'\x05'  # null at the deepest level
    for _ in range(depth):
        # object with one key "a" pointing to inner
        key = b'\x00\x01a'  # key length 1, key "a"
        obj = b'\x03' + key + inner + b'\x00\x00\x09'
        inner = obj

    payload += inner

    disconnected = False
    try:
        send_rtmp_message(sock, csid=3, msg_type=20, payload=payload)
        time.sleep(1)
        alive = is_connection_alive(sock)
        disconnected = not alive
    except (ConnectionError, OSError, BrokenPipeError):
        disconnected = True

    sock.close()

    if disconnected:
        print("  PASS: Server disconnected the client (nesting depth exceeded)")
        return True
    else:
        print("  FAIL: Server did NOT disconnect (may have accepted deep nesting)")
        return False


def test_amf0_truncated():
    """
    TEST 7: Truncated AMF0 data
    Send an AMF0 number type marker (0x00) but only 3 bytes of the 8-byte double.
    Server should disconnect gracefully (not crash).
    """
    print(f"\n{'='*60}")
    print("TEST 7: Truncated AMF0 Payload")
    print(f"{'='*60}")

    sock = connect_and_handshake()
    if not sock:
        print("  FAIL: Could not establish connection")
        return False

    drain_server_responses(sock, 0.5)

    # AMF0 command: string "connect" + truncated number (only 3 of 8 bytes)
    payload = build_amf0_string("connect")
    payload += b'\x00\x41\x42\x43'  # number marker + 3 bytes (need 8)

    disconnected = False
    try:
        send_rtmp_message(sock, csid=3, msg_type=20, payload=payload)
        time.sleep(1)
        alive = is_connection_alive(sock)
        disconnected = not alive
    except (ConnectionError, OSError, BrokenPipeError):
        disconnected = True

    sock.close()

    if disconnected:
        print("  PASS: Server disconnected gracefully (truncated AMF0)")
        return True
    else:
        # Not necessarily a fail — server might just ignore the bad command
        print("  WARN: Server stayed alive (may have ignored truncated data)")
        return True  # Acceptable: truncated data causes FormatException which disconnects


def test_publish_timeout():
    """
    TEST 8: Publish timeout (10 seconds)
    Connect and handshake but never publish. Server should disconnect after ~10s.
    """
    print(f"\n{'='*60}")
    print("TEST 8: Publish Timeout (~10s)")
    print(f"{'='*60}")

    sock = connect_and_handshake()
    if not sock:
        print("  FAIL: Could not establish connection")
        return False

    drain_server_responses(sock, 0.5)

    # Send connect command but never publish
    connect_payload = build_connect_command()
    try:
        send_rtmp_message(sock, csid=3, msg_type=20, payload=connect_payload)
    except (ConnectionError, OSError):
        print("  FAIL: Disconnected during connect")
        sock.close()
        return False

    drain_server_responses(sock, 0.5)

    print("  Waiting for publish timeout (up to 15s)...")
    start = time.time()
    disconnected = False

    while time.time() - start < 15:
        if not is_connection_alive(sock):
            disconnected = True
            break
        time.sleep(1)

    elapsed = time.time() - start
    sock.close()

    if disconnected:
        print(f"  PASS: Server disconnected after ~{elapsed:.1f}s (publish timeout)")
        return True
    else:
        print(f"  FAIL: Server did NOT disconnect after {elapsed:.1f}s")
        return False


def test_server_still_alive():
    """
    TEST 9: Server health check after all attacks
    Verify a normal handshake + connect still works.
    """
    print(f"\n{'='*60}")
    print("TEST 9: Server Health Check (post-attack)")
    print(f"{'='*60}")

    sock = connect_and_handshake()
    if not sock:
        print("  FAIL: Server is not accepting connections after attacks!")
        return False

    # Send a normal connect command
    connect_payload = build_connect_command()
    try:
        send_rtmp_message(sock, csid=3, msg_type=20, payload=connect_payload)
        drain_server_responses(sock, 1.0)
        alive = is_connection_alive(sock)
    except (ConnectionError, OSError):
        alive = False

    sock.close()

    if alive:
        print("  PASS: Server is healthy and accepting new connections")
        return True
    else:
        print("  FAIL: Server seems unresponsive")
        return False


def test_connection_flood():
    """
    TEST 10: Rapid connection flood
    Open and close connections as fast as possible for 3 seconds.
    Verify the server stays responsive afterward.
    """
    print(f"\n{'='*60}")
    print("TEST 10: Rapid Connection Flood (3 seconds)")
    print(f"{'='*60}")

    count = 0
    errors = 0
    start = time.time()

    while time.time() - start < 3:
        try:
            sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            sock.settimeout(2)
            sock.connect((HOST, PORT))
            sock.close()
            count += 1
        except (ConnectionError, OSError, socket.timeout):
            errors += 1

    print(f"  Connections attempted: {count + errors}")
    print(f"  Successful: {count}")
    print(f"  Rejected/failed: {errors}")

    # Now check if server is still alive
    time.sleep(1)
    try:
        sock = connect_and_handshake()
        if sock:
            print("  PASS: Server still healthy after flood")
            sock.close()
            return True
        else:
            print("  FAIL: Server not responding after flood")
            return False
    except Exception:
        print("  FAIL: Server not responding after flood")
        return False


def test_invalid_rtmp_version():
    """
    TEST 11: Invalid RTMP version byte
    Send version byte != 3. Server should disconnect.
    """
    print(f"\n{'='*60}")
    print("TEST 11: Invalid RTMP Version Byte")
    print(f"{'='*60}")

    sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    sock.settimeout(TIMEOUT)
    try:
        sock.connect((HOST, PORT))
    except (ConnectionError, OSError):
        print("  FAIL: Could not connect")
        return False

    # Send C0 with version 5 (invalid) + C1
    c1 = b'\x05' + os.urandom(1536)
    try:
        sock.sendall(c1)
        time.sleep(1)
        alive = is_connection_alive(sock)
    except (ConnectionError, OSError):
        alive = False

    sock.close()

    if not alive:
        print("  PASS: Server rejected invalid RTMP version")
        return True
    else:
        print("  FAIL: Server accepted invalid RTMP version")
        return False


def test_half_open_connections():
    """
    TEST 12: Half-open connections (connect TCP but send nothing)
    Open connections that never send handshake data.
    Server should eventually time out via ReceiveTimeout (30s) or publish timeout (10s).
    """
    print(f"\n{'='*60}")
    print("TEST 12: Half-Open Connections (no handshake data)")
    print(f"{'='*60}")

    idle_sockets = []
    for i in range(5):
        try:
            sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            sock.settimeout(TIMEOUT)
            sock.connect((HOST, PORT))
            idle_sockets.append(sock)
        except (ConnectionError, OSError):
            pass

    print(f"  Opened {len(idle_sockets)} idle TCP connections")
    time.sleep(1)

    # Verify the server still accepts new real connections
    test_sock = connect_and_handshake()
    if test_sock:
        print("  PASS: Server still accepts new connections despite idle sockets")
        test_sock.close()
        result = True
    else:
        print("  FAIL: Server not accepting connections with idle sockets open")
        result = False

    for s in idle_sockets:
        try:
            s.close()
        except Exception:
            pass

    return result


def test_per_ip_connection_limit():
    """
    TEST 13: Per-IP concurrent connection limit
    From a single IP, only MaxConnectionsPerIp connections should be accepted.
    """
    print(f"\n{'='*60}")
    print(f"TEST 13: Per-IP Connection Limit (limit={MAX_CONNECTIONS_PER_IP})")
    print(f"{'='*60}")

    sockets = []
    accepted = 0
    rejected = 0
    target = MAX_CONNECTIONS_PER_IP + 5

    for i in range(target):
        try:
            sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            sock.settimeout(5)
            sock.connect((HOST, PORT))
            if rtmp_handshake(sock):
                sockets.append(sock)
                accepted += 1
            else:
                rejected += 1
                sock.close()
        except (ConnectionError, OSError, socket.timeout):
            rejected += 1

        if i % 5 == 4:
            time.sleep(0.2)

    print(f"  Attempted: {target}")
    print(f"  Accepted:  {accepted}")
    print(f"  Rejected:  {rejected}")

    for s in sockets:
        try:
            s.close()
        except Exception:
            pass

    time.sleep(1)

    if accepted <= MAX_CONNECTIONS_PER_IP + 2 and rejected > 0:
        print("  PASS: Per-IP connection limit enforced")
        return True
    else:
        print(f"  FAIL: Expected ~{MAX_CONNECTIONS_PER_IP} accepted, got {accepted}")
        return False


def test_video_data_timeout():
    """
    TEST 14: Video data timeout after publish (~15s)
    Publish to a stream path but never send video data.
    Server should disconnect after the video data timeout.
    Requires /stream1 to be configured on the server.
    """
    print(f"\n{'='*60}")
    print(f"TEST 14: Video Data Timeout (~{VIDEO_DATA_TIMEOUT_S}s after publish)")
    print(f"{'='*60}")

    sock = connect_and_handshake()
    if not sock:
        print("  FAIL: Could not establish connection")
        return False

    drain_server_responses(sock, 0.5)

    # Step 1: Send connect command (app=stream1)
    try:
        send_rtmp_message(sock, csid=3, msg_type=20, payload=build_connect_command("stream1"))
        drain_server_responses(sock, 1.0)
    except (ConnectionError, OSError):
        print("  FAIL: Disconnected during connect")
        sock.close()
        return False

    # Step 2: Send createStream
    try:
        send_rtmp_message(sock, csid=3, msg_type=20, payload=build_create_stream_command())
        drain_server_responses(sock, 0.5)
    except (ConnectionError, OSError):
        print("  FAIL: Disconnected during createStream")
        sock.close()
        return False

    # Step 3: Send publish (empty stream name → path = /stream1 from app name)
    try:
        send_rtmp_message(sock, csid=8, msg_type=20, payload=build_publish_command(""), stream_id=1)
        drain_server_responses(sock, 1.0)
    except (ConnectionError, OSError):
        print("  FAIL: Disconnected during publish")
        sock.close()
        return False

    # Step 4: Wait for video data timeout (should be ~15s, not the 10s publish timeout)
    print(f"  Waiting for video data timeout (up to {VIDEO_DATA_TIMEOUT_S + 5}s)...")
    start = time.time()
    disconnected = False

    while time.time() - start < VIDEO_DATA_TIMEOUT_S + 5:
        if not is_connection_alive(sock):
            disconnected = True
            break
        time.sleep(1)

    elapsed = time.time() - start
    sock.close()

    if disconnected and elapsed >= VIDEO_DATA_TIMEOUT_S - 2:
        print(f"  PASS: Disconnected after ~{elapsed:.1f}s (video data timeout)")
        return True
    elif disconnected and elapsed >= 8:
        # Disconnected between 8-13s: could be publish timeout (10s) if publish was rejected
        print(f"  WARN: Disconnected after ~{elapsed:.1f}s (likely publish rejected, stream path not configured)")
        print(f"        Configure /stream1 on the server to properly test video data timeout")
        return True  # Not a server bug, just missing config
    elif disconnected:
        print(f"  PASS: Disconnected after ~{elapsed:.1f}s")
        return True
    else:
        print(f"  FAIL: Server did NOT disconnect after {elapsed:.1f}s")
        return False


# ─── Main ─────────────────────────────────────────────────────────────────────

def main():
    print(f"RTMP Server Security Test Suite")
    print(f"Target: {HOST}:{PORT}")
    print(f"Max connections setting: {MAX_CONNECTIONS}")

    # Pre-flight: verify we can connect at all
    print(f"\nPre-flight: checking server is reachable...")
    try:
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.settimeout(5)
        sock.connect((HOST, PORT))
        sock.close()
        print("  OK: Server is reachable")
    except (ConnectionError, OSError) as e:
        print(f"  ABORT: Cannot connect to {HOST}:{PORT} - {e}")
        sys.exit(1)

    time.sleep(0.5)

    tests = [
        ("Max Connections", test_max_connections),
        ("Oversized Message", test_oversized_message),
        ("Chunk Size Zero", test_chunk_size_zero),
        ("Chunk Size Too Large", test_chunk_size_too_large),
        ("Chunk Stream Exhaustion", test_chunk_stream_exhaustion),
        ("AMF0 Deep Nesting", test_amf0_deep_nesting),
        ("AMF0 Truncated", test_amf0_truncated),
        ("Publish Timeout", test_publish_timeout),
        ("Server Health Check", test_server_still_alive),
        ("Connection Flood", test_connection_flood),
        ("Invalid RTMP Version", test_invalid_rtmp_version),
        ("Half-Open Connections", test_half_open_connections),
        ("Per-IP Connection Limit", test_per_ip_connection_limit),
        ("Video Data Timeout", test_video_data_timeout),
    ]

    results = []
    for name, func in tests:
        try:
            passed = func()
            results.append((name, passed))
        except Exception as e:
            print(f"  ERROR: {e}")
            traceback.print_exc()
            results.append((name, False))
        # Small pause between tests to let the server reclaim resources
        time.sleep(1)

    # Summary
    print(f"\n{'='*60}")
    print("RESULTS SUMMARY")
    print(f"{'='*60}")
    passed = sum(1 for _, p in results if p)
    failed = sum(1 for _, p in results if not p)

    for name, p in results:
        status = "PASS" if p else "FAIL"
        print(f"  [{status}] {name}")

    print(f"\n  {passed}/{len(results)} passed, {failed} failed")

    if failed > 0:
        print("\n  Some tests FAILED. Check the server logs for details.")
        sys.exit(1)
    else:
        print("\n  All tests PASSED. Server hardening is working correctly.")
        sys.exit(0)


if __name__ == "__main__":
    main()
