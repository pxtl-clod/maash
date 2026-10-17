import sys
raw = sys.stdin.buffer.read()
sys.stderr.write(repr(raw))
