#!/usr/bin/env bash
# zd-book 随处调用薄壳(mac/Linux):把脚本目录内 zd_book.py 暴露成命令。
# 例:zd-book.sh import spec.json --out 账本.lbook
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec python3 "$DIR/zd_book.py" "$@"
