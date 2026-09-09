#!/usr/bin/env python3
"""Launch one isolated network batch and kill only its verified Unity authority."""
import argparse
import json
import os
from pathlib import Path
import signal
import socket
import subprocess
import sys
import time

SCENARIO = "UnexpectedHostLossMigrationScenario"
KILL_WAIT_SECONDS = 5


def atomic_json(path, value):
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_text(json.dumps(value), encoding="utf-8")
    os.replace(temporary, path)


def process_identity(pid):
    try:
        fields = Path(f"/proc/{pid}/stat").read_text().rsplit(")", 1)[1].split()
        return {"pid": pid, "state": fields[0], "parent": int(fields[1]),
                "group": int(fields[2]), "startTime": int(fields[19])}
    except (FileNotFoundError, ProcessLookupError):
        return None


def same_live_process(identity):
    current = process_identity(identity["pid"])
    return current and current["state"] != "Z" and current["startTime"] == identity["startTime"]


def owned_authority(identity_path, process):
    if process.poll() is not None:
        raise RuntimeError("Authority exited before crash injection")
    identity = json.loads(identity_path.read_text())
    if type(identity.get("pid")) is not int or identity["pid"] <= 1 or not same_live_process(identity):
        raise RuntimeError("Recorded authority process is no longer alive")
    current = process_identity(identity["pid"])
    if current["group"] != process.pid:
        raise RuntimeError("Authority PID is outside this runner's process group")
    ancestor = current
    while ancestor and ancestor["pid"] != process.pid:
        if ancestor["parent"] <= 1:
            raise RuntimeError("Authority PID is not a child of this runner's launch")
        ancestor = process_identity(ancestor["parent"])
    if not ancestor:
        raise RuntimeError("Authority ancestry changed before crash injection")
    return identity


def available_ports():
    sockets = []
    try:
        for _ in range(2):
            sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
            sock.bind(("127.0.0.1", 0))
            sockets.append(sock)
        return [sock.getsockname()[1] for sock in sockets]
    finally:
        for sock in sockets:
            sock.close()


def stop_owned_processes(processes):
    # Each Popen below creates its own session. No name-based or global process search.
    active = [process for process in processes if process.poll() is None]
    for process in active:
        try:
            os.killpg(process.pid, signal.SIGTERM)
        except ProcessLookupError:
            pass
    for process in active:
        try:
            process.wait(timeout=2)
        except subprocess.TimeoutExpired:
            try:
                os.killpg(process.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
            process.wait(timeout=2)


def supervise(args, send_kill=os.kill):
    if args.scenario != SCENARIO or args.players < 3 or args.mode not in ("server", "host"):
        raise RuntimeError("Invalid hard-crash scenario or insufficient players")
    results = Path(args.results).resolve()
    logs = Path(args.logs).resolve()
    ready = results / "migration-crash-ready.json"
    receipt = results / "migration-crash-injected.json"
    if ready.exists() or receipt.exists():
        raise RuntimeError("Refusing stale crash readiness or injection marker")
    initial_port, migration_port = available_ports()
    atomic_json(logs / "migration-endpoints.json", {"initialPort": initial_port, "migrationPort": migration_port})
    peer_count = args.players if args.mode == "server" else args.players - 1
    processes = []
    deadline = time.monotonic() + args.timeout
    script = str(Path(__file__).resolve())

    def launch(role, name):
        identity_path = logs / f"{name}-process.json"
        if identity_path.exists():
            raise RuntimeError("Refusing stale process identity record")
        command = ["xvfb-run", "-a", "--server-args=-screen 0 1024x768x24",
                   sys.executable, script, "--exec-player", str(identity_path), str(Path(args.binary).resolve()),
                   "-batchmode", "-nographics", "-role", role, "-scenario", SCENARIO,
                   "-port", str(initial_port), "-migrationPort", str(migration_port),
                   "-serverHost", "127.0.0.1", "-results", str(results / f"{name}.json"),
                   "-logFile", str(logs / f"{name}.log")]
        if role != "client":
            command.extend(["-count", str(args.players)])
        process = subprocess.Popen(command, start_new_session=True)
        processes.append(process)
        return process, identity_path

    try:
        authority, identity_path = launch(args.mode, args.mode)
        time.sleep(args.connect_delay)
        if authority.poll() is not None:
            raise RuntimeError("Authority exited before clients started")
        clients = [launch("client", f"client-{i}")[0] for i in range(1, peer_count + 1)]
        while True:
            if authority.poll() is not None:
                raise RuntimeError("Authority exited before crash injection")
            if any(client.poll() is not None for client in clients):
                raise RuntimeError("A survivor exited before crash injection")
            if ready.exists():
                marker = json.loads(ready.read_text())
                if not isinstance(marker, dict) or marker.get("scenario") != SCENARIO or marker.get("ready") is not True:
                    raise RuntimeError("Invalid authority crash readiness marker")
                break
            if time.monotonic() >= deadline:
                raise RuntimeError("Timed out waiting for authority crash readiness marker")
            time.sleep(0.05)

        if (results / f"{args.mode}.json").exists():
            raise RuntimeError("Authority produced final results before crash injection")
        identity = owned_authority(identity_path, authority)
        send_kill(identity["pid"], signal.SIGKILL)
        observation_deadline = time.monotonic() + KILL_WAIT_SECONDS
        while same_live_process(identity):
            if time.monotonic() >= observation_deadline:
                raise RuntimeError("Authority did not die after injected SIGKILL")
            time.sleep(0.01)
        authority_code = authority.wait(timeout=KILL_WAIT_SECONDS)
        if authority_code not in (-signal.SIGKILL, 128 + signal.SIGKILL):
            raise RuntimeError(f"Authority did not exit from SIGKILL: {authority_code}")
        atomic_json(receipt, {"scenario": SCENARIO, "injected": True, "authorityPid": identity["pid"]})
        print(f"Injected SIGKILL into owned authority PID {identity['pid']}", flush=True)
        for client in clients:
            remaining = max(0, deadline - time.monotonic())
            code = client.wait(timeout=remaining)
            if code != 0:
                raise RuntimeError(f"Survivor PID {client.pid} exited {code}")
        for index in range(1, peer_count + 1):
            result_path = results / f"client-{index}.json"
            if not result_path.is_file():
                raise RuntimeError(f"Missing survivor result file: {result_path.name}")
            rows = json.loads(result_path.read_text())
            if (not isinstance(rows, list) or any(not isinstance(row, dict) for row in rows) or
                    [row.get("name") for row in rows] != ["Bootstrap", SCENARIO] or
                    any(not isinstance(row.get("result"), dict) or row["result"].get("success") is not True for row in rows)):
                raise RuntimeError(f"Invalid or failed ordered survivor results: {result_path.name}")
        if (results / f"{args.mode}.json").exists():
            raise RuntimeError("Unexpected final authority JSON in hard-crash batch")
    finally:
        stop_owned_processes(processes)


def main():
    if len(sys.argv) > 1 and sys.argv[1] == "--exec-player":
        identity_path = Path(sys.argv[2])
        identity = process_identity(os.getpid())
        atomic_json(identity_path, {"pid": identity["pid"], "startTime": identity["startTime"]})
        os.execv(sys.argv[3], sys.argv[3:])
    parser = argparse.ArgumentParser()
    parser.add_argument("--binary", required=True)
    parser.add_argument("--mode", required=True)
    parser.add_argument("--players", type=int, required=True)
    parser.add_argument("--scenario", required=True)
    parser.add_argument("--results", required=True)
    parser.add_argument("--logs", required=True)
    parser.add_argument("--timeout", type=float, required=True)
    parser.add_argument("--connect-delay", type=float, default=1)
    args = parser.parse_args()
    def interrupted(signum, frame):
        raise RuntimeError(f"Host-loss runner interrupted by signal {signum}")
    signal.signal(signal.SIGTERM, interrupted)
    signal.signal(signal.SIGINT, interrupted)
    try:
        supervise(args)
    except (RuntimeError, OSError, ValueError, subprocess.TimeoutExpired) as error:
        print(f"::error::{error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
