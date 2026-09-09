#!/usr/bin/env python3
"""Linux process tests for hard-crash injection, using owned mock players only."""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

SCRIPT_DIR = Path(__file__).resolve().parent
REAL_XVFB = shutil.which("xvfb-run")
sys.dont_write_bytecode = True
SPEC = importlib.util.spec_from_file_location("host_loss", SCRIPT_DIR / "run-host-loss.py")
runner = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(runner)

MOCK_PLAYER = r'''#!/usr/bin/env python3
import json, os, sys, time
from pathlib import Path
args = sys.argv[1:]
def arg(name): return args[args.index(name) + 1]
role = arg('-role')
result = Path(arg('-results'))
log = Path(arg('-logFile'))
name = result.stem
fault = os.environ.get('MOCK_FAULT', '')
log.write_text('Mock player running\n')
if arg('-port') == arg('-migrationPort'):
    sys.exit(91)
(log.parent / (name + '.started')).touch()
if role != 'client':
    if fault == 'early-exit': sys.exit(17)
    count = int(arg('-count')) - (1 if role == 'host' else 0)
    while len(list(log.parent.glob('client-*.started'))) < count: time.sleep(.01)
    if fault == 'unowned-pid':
        pid = int(os.environ['FOREIGN_PID'])
        fields = Path('/proc/' + str(pid) + '/stat').read_text().rsplit(')', 1)[1].split()
        (log.parent / (name + '-process.json')).write_text(json.dumps({'pid':pid,'startTime':int(fields[19])}))
    if fault != 'missing-marker':
        marker = {'scenario':arg('-scenario'), 'ready':True}
        if fault == 'wrong-marker': marker['scenario'] = 'WrongScenario'
        if fault == 'false-marker': marker['ready'] = False
        temporary = result.parent / 'ready.tmp'
        temporary.write_text(json.dumps(marker))
        os.replace(temporary, result.parent / 'migration-crash-ready.json')
    if fault == 'early-ready-exit': sys.exit(18)
    while True: time.sleep(.02)
receipt = result.parent / 'migration-crash-injected.json'
while not receipt.exists(): time.sleep(.01)
received = json.loads(receipt.read_text())
if received.get('injected') is not True or received.get('scenario') != arg('-scenario'): sys.exit(92)
if name == 'client-1' and fault == 'exit-nonzero': sys.exit(19)
if name != 'client-1' or fault != 'missing-client':
    rows = [{'name':'Bootstrap', 'result':{'success':True}},
            {'name':arg('-scenario'), 'result':{'success':True}}]
    if name == 'client-1' and fault == 'failed-client': rows[1]['result']['success'] = False
    if name == 'client-1' and fault == 'wrong-order': rows.reverse()
    result.write_text(json.dumps(rows))
'''


@unittest.skipUnless(sys.platform == "linux", "Requires Linux /proc and POSIX process groups")
class HostLossTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(prefix="purrnet-host-loss-")
        self.root = Path(self.directory.name)
        self.results = self.root / "results"
        self.logs = self.root / "logs"
        self.bin = self.root / "bin"
        for path in (self.results, self.logs, self.bin):
            path.mkdir()
        xvfb = self.bin / "xvfb-run"
        xvfb.write_text("#!/usr/bin/env python3\nimport os,sys\na=sys.argv[1:]\nwhile a[0].startswith('-'): a.pop(0)\nos.execvp(a[0],a)\n")
        xvfb.chmod(0o755)
        player = self.bin / "player"
        player.write_text(MOCK_PLAYER)
        player.chmod(0o755)
        self.environment = patch.dict(os.environ, {"PATH": str(self.bin) + os.pathsep + os.environ["PATH"], "MOCK_FAULT": ""})
        self.environment.start()
        self.kill_wait = patch.object(runner, "KILL_WAIT_SECONDS", 1)
        self.kill_wait.start()
        self.args = argparse.Namespace(binary=str(player), mode="host", players=3, scenario=runner.SCENARIO,
                                       results=str(self.results), logs=str(self.logs), timeout=3, connect_delay=0)

    def tearDown(self):
        self.kill_wait.stop()
        self.environment.stop()
        self.directory.cleanup()

    def run_fault(self, fault, expected, injected=False):
        os.environ["MOCK_FAULT"] = fault
        with self.assertRaisesRegex((RuntimeError, subprocess.TimeoutExpired), expected):
            runner.supervise(self.args)
        self.assertEqual((self.results / "migration-crash-injected.json").exists(), injected)

    def assert_success(self, mode, count):
        self.args.mode = mode
        runner.supervise(self.args)
        receipt = json.loads((self.results / "migration-crash-injected.json").read_text())
        self.assertEqual(receipt["scenario"], runner.SCENARIO)
        self.assertIs(receipt["injected"], True)
        self.assertFalse((self.results / (mode + ".json")).exists())
        self.assertEqual(len(list(self.results.glob("client-*.json"))), count)
        endpoints = json.loads((self.logs / "migration-endpoints.json").read_text())
        self.assertNotEqual(endpoints["initialPort"], endpoints["migrationPort"])
        for path in self.logs.glob("*-process.json"):
            self.assertFalse(runner.same_live_process(json.loads(path.read_text())))

    def test_host_survivors_pass_only_after_actual_kill(self):
        self.assert_success("host", 2)

    def test_server_survivors_pass_only_after_actual_kill(self):
        self.assert_success("server", 3)

    @unittest.skipUnless(REAL_XVFB, "Requires xvfb-run")
    def test_real_xvfb_wrapper_preserves_verified_unity_pid_and_kill_exit(self):
        self.args.timeout = 15
        with patch.dict(os.environ, {"PATH": os.path.dirname(REAL_XVFB) + os.pathsep + os.environ["PATH"]}):
            self.assert_success("host", 2)

    def test_missing_ready_marker_fails_without_injection(self):
        self.args.timeout = .5
        self.run_fault("missing-marker", "Timed out waiting")

    def test_early_authority_exit_is_not_an_injected_crash(self):
        self.run_fault("early-exit", "Authority exited")

    def test_authority_exit_after_ready_is_not_an_injected_crash(self):
        self.run_fault("early-ready-exit", "Authority exited|no longer alive|did not exit from SIGKILL")

    def test_wrong_marker_fails_without_injection(self):
        self.run_fault("wrong-marker", "Invalid authority crash readiness marker")

    def test_false_readiness_fails_without_injection(self):
        self.run_fault("false-marker", "Invalid authority crash readiness marker")

    def test_no_actual_kill_cannot_create_receipt(self):
        with self.assertRaisesRegex(RuntimeError, "did not die after injected SIGKILL"):
            runner.supervise(self.args, send_kill=lambda pid, signal: None)
        self.assertFalse((self.results / "migration-crash-injected.json").exists())

    def test_missing_survivor_json_fails(self):
        self.run_fault("missing-client", "Missing survivor result file", injected=True)

    def test_failed_survivor_json_fails(self):
        self.run_fault("failed-client", "Invalid or failed ordered survivor results", injected=True)

    def test_wrong_survivor_order_fails(self):
        self.run_fault("wrong-order", "Invalid or failed ordered survivor results", injected=True)

    def test_nonzero_survivor_exit_fails(self):
        self.run_fault("exit-nonzero", "Survivor PID .* exited", injected=True)

    def test_stale_ready_marker_is_refused(self):
        (self.results / "migration-crash-ready.json").write_text(json.dumps({"scenario":runner.SCENARIO,"ready":True}))
        self.run_fault("", "Refusing stale")
        self.assertFalse(list(self.logs.glob("*-process.json")))

    def test_stale_receipt_is_refused(self):
        (self.results / "migration-crash-injected.json").write_text(json.dumps({"scenario":runner.SCENARIO,"injected":True}))
        self.run_fault("", "Refusing stale", injected=True)
        self.assertFalse(list(self.logs.glob("*-process.json")))

    def test_foreign_pid_is_never_killed(self):
        foreign = subprocess.Popen([sys.executable, "-c", "import time; time.sleep(30)"])
        try:
            os.environ["FOREIGN_PID"] = str(foreign.pid)
            self.run_fault("unowned-pid", "outside this runner's process group")
            self.assertIsNone(foreign.poll())
        finally:
            foreign.terminate()
            foreign.wait(timeout=2)

    def test_fault_mode_is_registered_once_with_required_players(self):
        manifest = json.loads((SCRIPT_DIR.parent / "network-scenarios.json").read_text())
        fault_runs = [run for run in manifest["runs"] if run.get("fault")]
        self.assertEqual(len(fault_runs), 1)
        self.assertEqual(fault_runs[0]["scenario"], runner.SCENARIO)
        self.assertEqual(fault_runs[0]["fault"], "kill-authority")
        self.assertEqual(fault_runs[0]["minimumPlayers"], 3)


if __name__ == "__main__":
    unittest.main(verbosity=2)
