import sys

# Read header using buffer directly (not sys.stdin.readline) to avoid
# the TextIOWrapper buffering issue where it consumes the operand bytes.
line = sys.stdin.buffer.readline().decode().split()
if not line or line[0] != "M1":
    sys.exit(1)

op = line[1]
blen = int(line[2])
raw = sys.stdin.buffer.read(blen)
NL = chr(10)

# The raw operand may have a leading space (implicit separator after operator).
# We strip it for processing but report the FULL operand length (including stripped
# spaces) so the Evaluator can correctly advance its position in the source line.
operand_raw = raw.decode("utf-8")
stripped = operand_raw.lstrip(" ")

# Operator dispatch: the suffix maash attached decides behavior regardless of what
# infix is present (so @@, @@+, @@<infix>@, @@<infix>+ all resolve here).
def reply(parsed_len, value):
    sys.stdout.write("M1 %d %d%s%s" % (parsed_len, len(value.encode("utf-8")), NL, value))

# op looks like "@@", "@@+", "@@ech@", "@@ech+".
# Take the last token after the final "@" group: simplest = check trailing char.
if op.endswith("+"):
    reply(len(operand_raw), stripped.upper())
else:
    # Primary (echo) forms: @@, @@<infix>@, and any non-+ operator.
    reply(len(operand_raw), stripped)
