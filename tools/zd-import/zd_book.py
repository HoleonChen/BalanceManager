#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""zd_book —— 账本导入便捷脚本(独立于应用本体,零第三方依赖)。

数据规范见同目录 SPEC.md。底层加密建库由 ZhangDan.Sealer(.NET,复用主程序同款
SQLCipher)完成;本脚本负责 agent 友好的 lint/汇总/口令与调用。

用法:
  zd_book.py lint <spec.json>                       # 只校验+汇总(agent 迭代)
  zd_book.py sample [> 文件]                        # 打印示例 spec
  zd_book.py import <spec.json|-> --out 账本.lbook [--name 账本名] [--password …]

口令优先级:--password > 环境变量 ZD_BOOK_PASSWORD > 终端交互。
密封器定位:环境变量 ZD_BOOK_SEALER > tools/ZhangDan.Sealer 的 Release 产物
           (找不到会自动 dotnet build -c Release 生成)。
"""
import getpass
import json
import os
import subprocess
import sys
import tempfile
from datetime import datetime
from pathlib import Path

SCRIPT_DIR = Path(__file__).resolve().parent
PROJECT_ROOT = SCRIPT_DIR.parents[1]          # 仓库根
EXAMPLE_JSON = SCRIPT_DIR / "example" / "示例账本.json"
SEALER_DIR = PROJECT_ROOT / "tools" / "ZhangDan.Sealer"
SEALER_CFG = SEALER_DIR / "ZhangDan.Sealer.csproj"

VALID_ACCOUNT_TYPES = {"wallet", "money_fund", "bank", "cash", "fixed_deposit", "fund", "prepaid"}
VALID_DIRECTIONS = {"in", "out", "transfer"}
VALID_KINDS = {"expense", "income"}


# ---------------------------------------------------------------- 读/存
def load_spec(spec_arg):
    if spec_arg in ("-", "--stdin"):
        return json.load(sys.stdin)
    return json.loads(Path(spec_arg).read_text(encoding="utf-8"))


def yuan(v):
    try:
        return f"{float(v):.2f}"
    except (TypeError, ValueError):
        return "?"


def _date_range(txns):
    dates = [t.get("date") for t in txns if isinstance(t, dict) and t.get("date")]
    if not dates:
        return None, None
    return min(dates), max(dates)


# ---------------------------------------------------------------- lint
def lint(spec, strict=True):
    """轻量校验+汇总。返回 (ok:bool, 行列表)。硬性错误置 ok=False。"""
    lines = []
    if not isinstance(spec, dict):
        return False, ["顶层必须是 JSON 对象。"]

    accounts = spec.get("accounts") or []
    categories = spec.get("categories") or []
    periods = spec.get("periods") or []
    pools = spec.get("fundPools") or []
    txns = spec.get("transactions") or []
    for name in ("accounts", "categories", "periods", "fundPools", "transactions"):
        if not isinstance(spec.get(name, []), list):
            return False, [f"「{name}」应为数组。"]

    ok = True
    acct_names, seen = [], set()
    for i, a in enumerate(accounts, 1):
        nm = a.get("name")
        if not isinstance(a, dict) or not nm:
            ok = False
            lines.append(f"错误 accounts[{i}]:缺 name 或不是对象")
        elif nm in seen:
            ok = False
            lines.append(f"错误 accounts[{i}]:账户名重复「{nm}」")
        else:
            seen.add(nm)
            acct_names.append(nm)
            t = a.get("type", "wallet")
            if t not in VALID_ACCOUNT_TYPES:
                ok = False
                lines.append(f"错误 accounts[{i}] {nm}:type「{t}」非法")

    cat_seen = set()
    for i, c in enumerate(categories, 1):
        nm, kind = c.get("name"), c.get("kind")
        if not nm:
            ok = False
            lines.append(f"错误 categories[{i}]:缺 name")
        elif kind not in VALID_KINDS:
            ok = False
            lines.append(f"错误 categories[{i}] {nm}:kind 应为 expense/income")
        else:
            key = (kind, nm)
            if key in cat_seen:
                ok = False
                lines.append(f"错误 categories[{i}]:{kind} 分类名重复「{nm}」")
            cat_seen.add(key)

    per_seen = set()
    for i, p in enumerate(periods, 1):
        nm = p.get("name")
        if not nm or nm in per_seen:
            ok = False
            lines.append(f"错误 periods[{i}]:周期名缺失或重复")
        per_seen.add(nm)

    for i, fp in enumerate(pools, 1):
        if fp.get("period") not in per_seen:
            ok = False
            lines.append(f"错误 fundPools[{i}]:引用了未定义周期「{fp.get('period')}」")

    txn_issues = 0
    for i, t in enumerate(txns, 1):
        if not isinstance(t, dict):
            ok = False
            lines.append(f"错误 transactions[{i}]:不是对象")
            continue
        d, dir_, acct, amt = t.get("date"), t.get("direction"), t.get("account"), t.get("amount")
        if not d or not isinstance(d, str):
            ok = False
            lines.append(f"错误 transactions[{i}]:缺 date")
        if dir_ not in VALID_DIRECTIONS:
            ok = False
            lines.append(f"错误 transactions[{i}]:direction「{dir_}」非法")
        if acct not in acct_names:
            ok = False
            lines.append(f"错误 transactions[{i}]:账户「{acct}」未定义")
        try:
            if dir_ == "transfer":
                if float(amt) <= 0:
                    ok = False
                    lines.append(f"错误 transactions[{i}]:transfer 本金需 >0")
            elif float(amt) is not None and float(amt) <= 0:
                ok = False
                lines.append(f"错误 transactions[{i}]:amount 需 >0")
        except (TypeError, ValueError):
            ok = False
            lines.append(f"错误 transactions[{i}]:amount 非数字")
        if dir_ == "transfer" and not t.get("toAccount"):
            ok = False
            lines.append(f"错误 transactions[{i}]:transfer 缺 toAccount")
        if not ok and txn_issues < 1:
            txn_issues += 1
        cat = t.get("category")
        if cat and dir_ in ("in", "out"):
            want = "income" if dir_ == "in" else "expense"
            if (want, cat) not in cat_seen:
                lines.append(f"提示 transactions[{i}]:分类「{cat}」不在 {want} 分类里(可能是未定义或方向不匹配)")

    # 汇总
    lo, hi = _date_range(txns)
    lines.insert(0, f"账户 {len(accounts)} · 分类 {len(categories)} · 周期 {len(periods)}"
                    f" · 资金池 {len(pools)} · 流水 {len(txns)} 笔"
                    + (f" · 日期 {lo} ~ {hi}" if lo else ""))
    if not ok and strict:
        lines.insert(0, "校验未通过(供密封器做最终硬校验):")
    return ok, lines


# ---------------------------------------------------------------- sealer
def sealer_command():
    env = os.environ.get("ZD_BOOK_SEALER")
    if env:
        p = Path(env)
        return p, "env ZD_BOOK_SEALER"
    exe = "ZhangDan.Sealer.exe" if os.name == "nt" else "ZhangDan.Sealer"
    p = SEALER_DIR / "bin" / "Release" / "net8.0" / exe
    if p.exists():
        return p, p
    # 自动构建一次
    print(f"[zd_book] 未找到密封器 {p},尝试 dotnet build -c Release …", file=sys.stderr)
    r = subprocess.run(["dotnet", "build", "-c", "Release", str(SEALER_CFG)],
                       capture_output=True, text=True)
    if r.returncode != 0 or not p.exists():
        print(r.stderr[-2000:], file=sys.stderr)
        raise SystemExit(
            "无法生成密封器。请确认装了 .NET SDK 后手动执行:\n"
            f"  dotnet build -c Release {SEALER_CFG}\n"
            "或把构建产物路径设到环境变量 ZD_BOOK_SEALER。")
    return p, p


def run_sealer(args):
    binary, _src = sealer_command()
    try:
        return subprocess.run([str(binary), *args], capture_output=True)
    except OSError:
        # apphost 不可执行时退回 dotnet <dll>
        dll = binary.with_suffix(".dll")
        env = dict(os.environ, DOTNET_ROLL_FORWARD="LatestMajor")
        return subprocess.run(["dotnet", str(dll), *args], capture_output=True, env=env)


def resolve_password(pw):
    if pw:
        return pw
    env = os.environ.get("ZD_BOOK_PASSWORD")
    if env:
        return env
    if sys.stdin.isatty():
        return getpass.getpass("账本口令(不回显): ")
    raise SystemExit("未提供口令:用 --password,或设环境变量 ZD_BOOK_PASSWORD,或在终端运行。")


def _print_sealer_output(r):
    out = r.stdout.decode("utf-8", errors="replace").strip()
    err = r.stderr.decode("utf-8", errors="replace").strip()
    if out:
        print(out)
    if err:
        print(err, file=sys.stderr)


# ---------------------------------------------------------------- subcommands
def cmd_lint(spec_arg):
    try:
        spec = load_spec(spec_arg)
    except Exception as ex:
        print(f"JSON 解析失败:{ex}", file=sys.stderr)
        return 1
    ok, lines = lint(spec)
    print("\n".join(lines))
    return 0 if ok else 1


def cmd_sample():
    print(EXAMPLE_JSON.read_text(encoding="utf-8"), end="")


def cmd_import(spec_arg, out, name, pw):
    if not out:
        raise SystemExit("缺 --out <账本.lbook>")
    tmp = None
    try:
        if spec_arg in ("-", "--stdin"):
            f = tempfile.NamedTemporaryFile("w", suffix=".json", delete=False, encoding="utf-8")
            tmp = f.name
            f.write(sys.stdin.read())
            f.close()
            spec_path = tmp
        else:
            spec_path = spec_arg
        try:
            spec = load_spec(spec_path)
        except Exception as ex:
            raise SystemExit(f"JSON 解析失败:{ex}")
        ok, lines = lint(spec)
        print("\n".join(lines))
        if not ok:
            raise SystemExit("先修正上述问题;最终以密封器硬校验为准。")
        password = resolve_password(pw)
        args = ["import", spec_path, "--out", out]
        if name:
            args += ["--name", name]
        args += ["--password", password]
        r = run_sealer(args)
        _print_sealer_output(r)
        return r.returncode
    finally:
        if tmp:
            try:
                os.unlink(tmp)
            except OSError:
                pass


def main(argv):
    if len(argv) < 1 or argv[0] in ("-h", "--help", "help"):
        print(__doc__)
        return 0 if argv else 2

    cmd = argv[0]
    rest = argv[1:]
    if cmd == "sample":
        if rest:
            print("sample 无参数;用法: zd_book.py sample > 文件", file=sys.stderr)
            return 2
        cmd_sample()
        return 0
    if cmd == "lint":
        if len(rest) != 1:
            print("用法: zd_book.py lint <spec.json>", file=sys.stderr)
            return 2
        return cmd_lint(rest[0])
    if cmd == "import":
        # import <spec|- -> [--out f] [--name n] [--password p]
        spec, out, name, pw = None, None, None, None
        i = 0
        while i < len(rest):
            a = rest[i]
            if a == "--out" and i + 1 < len(rest):
                out = rest[i + 1]; i += 2
            elif a == "--name" and i + 1 < len(rest):
                name = rest[i + 1]; i += 2
            elif a == "--password" and i + 1 < len(rest):
                pw = rest[i + 1]; i += 2
            elif spec is None:
                spec = a; i += 1
            else:
                print(f"多余参数:{a}", file=sys.stderr)
                return 2
        if spec is None:
            print("用法: zd_book.py import <spec.json|-> --out 账本.lbook", file=sys.stderr)
            return 2
        return cmd_import(spec, out, name, pw)
    print(f"未知子命令:{cmd}\n{__doc__}", file=sys.stderr)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
