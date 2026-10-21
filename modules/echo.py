#!/usr/bin/env python3
"""maash echo module (fixture) implementing the M1 protocol.

Reads the M1 header from stdin using buffer reads (robust against any
subtle stdin buffering interactions), dispatches on the operator's trailing
character, and reports the parsed character-length so the scanner resumes
right after the consumed operand text.

Dispatch:
  op ending '+' or 'x'  -> UPPER (upper-cases the operand)
  otherwise             -> ECHO (returns operand as-is)

The trailing '+' also distinguishes '@@+' (upper) from '@@' (echo) for the
primary/echo fixture.  For alnum-infix forms the primary-suffix operator
'@@<infix>@' has the bare 'x' form echo and '@@<infix>+' forms upper
(echo.py's trailing-char dispatch handles both without extra config).
"""
import sys

def main():
    # Read exactly one M1 request: header + <operandLen> bytes.
    # We buffer via raw reads to avoid line-buffering edge cases and to ensure
    # all <operandLen> payload bytes arrive even without a trailing newline.
    data = b""
    while b"\n" not in data:
        chunk = sys.stdin.buffer.read(1024)
        if not chunk:
            break
        data += chunk
    header, _, rest = data.partition(b"\n")
    try:
        parts = header.decode("utf-8").split()
        if parts[0] != "M1":
            return 3
        op = parts[1].decode("utf-8", "replace") if isinstance(parts[1], bytes) else parts[1]
        blen = int(parts[2])
    except (IndexError, ValueError):
        return 3

    operand_bytes = rest[:blen]
    while len(operand_bytes) < blen:
        chunk = sys.stdin.buffer.read(4096)
        if not chunk:
            break
        operand_bytes += chunk

    op_str = op if isinstance(op, str) else op.decode("utf-8", "replace")
    # Parsed length: number of chars of the RAW operand the module consumed
    # (leading whitespace included; that's the range maash must replace).
    # The module consumes the entire operand here, so report its full length.
    parsed_char_len = len(operand_bytes.decode("utf-8", "replace"))
    op_text = operand_bytes.decode("utf-8", "replace").lstrip(" ")

    if op_str.endswith("+") or op_str.endswith("x"):
        out = op_text.upper()
    else:
        out = op_text
    out_bytes = out.encode("utf-8")
    sys.stdout.buffer.write(b"M1 %d %d\n" % (parsed_char_len, len(out_bytes)))
    sys.stdout.buffer.write(out_bytes)
    sys.stdout.buffer.flush()
    return 0

if __name__ == "__main__":
    sys.exit(main())
