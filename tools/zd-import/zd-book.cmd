@echo off
rem zd-book 随处调用薄壳(Windows):把脚本目录内 zd_book.py 暴露成命令。
rem 例:zd-book.cmd import spec.json --out 账本.lbook
python "%~dp0zd_book.py" %*
