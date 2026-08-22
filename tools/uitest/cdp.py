"""Minimal Chrome DevTools Protocol client — enough to send TRUSTED input events.

Why this exists: synthetic `dispatchEvent(new PointerEvent(...))` events do not run default
actions, so they cannot reproduce (or disprove) any bug about focus, selection or compatibility
mouse events. CDP's Input domain injects events at the browser level, which do. No websocket
library is installed on this box, so the frame handling is done by hand: text frames only,
client-to-server masked, server-to-client unmasked, which is all the protocol needs here.

Usage:
    python cdp.py <file-url> [drive]
"""
import base64
import json
import os
import socket
import struct
import subprocess
import sys
import tempfile
import time
import urllib.request

CHROME = r'C:\Program Files\Google\Chrome\Application\chrome.exe'
PORT = 9333


class WS:
    def __init__(self, url):
        # ws://127.0.0.1:PORT/devtools/page/<id>
        rest = url.split('://', 1)[1]
        hostport, path = rest.split('/', 1)
        host, port = hostport.split(':')
        self.sock = socket.create_connection((host, int(port)))
        key = base64.b64encode(os.urandom(16)).decode()
        req = (
            'GET /%s HTTP/1.1\r\nHost: %s\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n'
            'Sec-WebSocket-Key: %s\r\nSec-WebSocket-Version: 13\r\n\r\n' % (path, hostport, key)
        )
        self.sock.sendall(req.encode())
        self.buf = b''
        while b'\r\n\r\n' not in self.buf:
            self.buf += self.sock.recv(4096)
        head, self.buf = self.buf.split(b'\r\n\r\n', 1)
        assert b'101' in head.split(b'\r\n')[0], head

    def send(self, obj):
        payload = json.dumps(obj).encode()
        mask = os.urandom(4)
        masked = bytes(b ^ mask[i % 4] for i, b in enumerate(payload))
        n = len(payload)
        if n < 126:
            header = struct.pack('!BB', 0x81, 0x80 | n)
        elif n < 65536:
            header = struct.pack('!BBH', 0x81, 0x80 | 126, n)
        else:
            header = struct.pack('!BBQ', 0x81, 0x80 | 127, n)
        self.sock.sendall(header + mask + masked)

    def _read(self, n):
        while len(self.buf) < n:
            chunk = self.sock.recv(65536)
            if not chunk:
                raise EOFError('socket closed')
            self.buf += chunk
        out, self.buf = self.buf[:n], self.buf[n:]
        return out

    def recv(self):
        b0, b1 = self._read(2)
        length = b1 & 0x7F
        if length == 126:
            length = struct.unpack('!H', self._read(2))[0]
        elif length == 127:
            length = struct.unpack('!Q', self._read(8))[0]
        data = self._read(length)
        if (b0 & 0x0F) != 1:  # ignore non-text frames (ping/close)
            return None
        return json.loads(data.decode('utf-8', 'replace'))

    def call(self, method, params=None, _id=[0], timeout=15):
        _id[0] += 1
        mid = _id[0]
        self.send({'id': mid, 'method': method, 'params': params or {}})
        deadline = time.time() + timeout
        while time.time() < deadline:
            self.sock.settimeout(max(0.1, deadline - time.time()))
            msg = self.recv()
            if msg and msg.get('id') == mid:
                if 'error' in msg:
                    raise RuntimeError('%s -> %s' % (method, msg['error']))
                return msg.get('result', {})
        raise TimeoutError(method)


def launch(url):
    profile = tempfile.mkdtemp(prefix='cdp-')
    proc = subprocess.Popen([
        CHROME, '--headless=new', '--disable-gpu', '--hide-scrollbars',
        '--window-size=1080,720', '--remote-debugging-port=%d' % PORT,
        '--user-data-dir=' + profile, '--no-first-run', '--no-default-browser-check',
        url,
    ], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    for _ in range(100):
        try:
            raw = urllib.request.urlopen('http://127.0.0.1:%d/json' % PORT, timeout=1).read()
            targets = [t for t in json.loads(raw) if t.get('type') == 'page' and t.get('webSocketDebuggerUrl')]
            if targets:
                return proc, WS(targets[0]['webSocketDebuggerUrl'])
        except Exception:
            time.sleep(0.2)
    proc.kill()
    raise RuntimeError('no CDP page target')


def js(ws, expr):
    r = ws.call('Runtime.evaluate', {'expression': expr, 'returnByValue': True, 'awaitPromise': True})
    return r.get('result', {}).get('value')


def click(ws, x, y):
    for kind in ('mousePressed', 'mouseReleased'):
        ws.call('Input.dispatchMouseEvent', {
            'type': kind, 'x': x, 'y': y, 'button': 'left', 'clickCount': 1, 'buttons': 1,
        })
        time.sleep(0.05)


def drag(ws, points):
    ws.call('Input.dispatchMouseEvent', {'type': 'mousePressed', 'x': points[0][0], 'y': points[0][1],
                                         'button': 'left', 'clickCount': 1, 'buttons': 1})
    for (x, y) in points[1:]:
        ws.call('Input.dispatchMouseEvent', {'type': 'mouseMoved', 'x': x, 'y': y,
                                             'button': 'left', 'buttons': 1})
    last = points[-1]
    ws.call('Input.dispatchMouseEvent', {'type': 'mouseReleased', 'x': last[0], 'y': last[1],
                                         'button': 'left', 'clickCount': 1, 'buttons': 0})


def type_text(ws, text):
    ws.call('Input.insertText', {'text': text})


def key(ws, code, key_name, windows_code):
    for kind in ('keyDown', 'keyUp'):
        ws.call('Input.dispatchKeyEvent', {'type': kind, 'code': code, 'key': key_name,
                                           'windowsVirtualKeyCode': windows_code})


def screenshot(ws, path):
    r = ws.call('Page.captureScreenshot', {'format': 'png'})
    open(path, 'wb').write(base64.b64decode(r['data']))
