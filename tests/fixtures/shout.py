#!/usr/bin/env python3
"""shout module: uppercases operand."""
import sys
def main():
    data = b""
    while b"\n" not in data:
        chunk = sys.stdin.buffer.read(1024)
        if not chunk: break
        data += chunk
    header, _, rest = data.partition(b"\n")
    parts = header.decode().split()
    if not parts or parts[0] != "M1":
        return 3
    blen = int(parts[2])
    operand_bytes = rest[:blen]
    while len(operand_bytes) < blen:
        chunk = sys.stdin.buffer.read(4096)
        if not chunk: break
        operand_bytes += chunk
    op = parts[1]
    text = operand_bytes.decode("utf-8", "replace").strip()
    if op.endswith("#"):
        out = text.upper() + " " + text.upper()
    else:
        out = text.upper()
    b = out.encode()
    sys.stdout.buffer.write(b"M1 %d %d\n" % (len(text), len(b)))
    sys.stdout.buffer.write(b)
    sys.stdout.buffer.flush()
    return 0
if __name__ == "__main__":
    sys.exit(main())
