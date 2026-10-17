#!/usr/bin/env bash
# maash M1 protocol handler for the 'echo' module (infix @, loaded via -i @)
# Operators: @@ (echo), @@+ (upper), @@~ (bracketed upper)
# Request  (stdin):  M1 <opString> <operandByteLen>\n<operand bytes>
# Response (stdout): M1 <parsedCharLen> <outputByteLen>\n<output bytes>

read -r tag opString operandByteLen
if [ "$tag" != "M1" ]; then echo "bad header" >&2; exit 1; fi

operand=""
if [ -n "$operandByteLen" ] && [ "$operandByteLen" -gt 0 ] 2>/dev/null; then
    IFS= read -r -N "$operandByteLen" operand || true
fi

case "$opString" in
  @@)   # primary echo
    printf 'M1 %d %d\n%s' "${#operand}" "${#operand}" "$operand"
    ;;
  @@+)  # upper case
    u=$(printf '%s' "$operand" | tr 'a-z' 'A-Z')
    printf 'M1 %d %d\n%s' "${#operand}" "${#u}" "$u"
    ;;
  @@~)  # bracketed upper
    b=$(printf '%s' "$operand" | tr 'a-z' 'A-Z')
    printf 'M1 %d %d\n%s' "${#b}" "${#b}" "$b"
    ;;
  *)
    echo "unknown: $opString" >&2
    exit 1
    ;;
esac
exit 0
