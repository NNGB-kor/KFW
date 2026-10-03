"""Developer-only test: mocked Linux commands, no SSH server or hardware access."""
import contextlib
import copy
import io
import json
import os
import pathlib
import subprocess
import sys
import tempfile
import time
from unittest.mock import patch

source = pathlib.Path(sys.argv[1]).read_text(encoding='utf-8')
space = {'__name__': 'test_runner'}
exec(compile(source, 'runner.py', 'exec'), space)
original = copy.deepcopy(space['cfg'])
secret = 'fake-test-only-$quote\'"-password'
assert secret not in source
results = {}

def scenario(name, active=True, stop=0, start=0, flash=0, enabled=True, marker=True, raises=False, check=True, returned=True):
    calls = []
    space['cfg'] = dict(original, stop=enabled, check=check)
    def run(args, **kwargs):
        calls.append((args, kwargs))
        assert secret not in ' '.join(args)
        if args[0] == 'sudo':
            assert args[:6] == ['sudo', '-S', '-p', '', '--', 'systemctl']
            assert kwargs['input'] == secret + '\n'
            rc = stop if args[6] == 'stop' else start
            return subprocess.CompletedProcess(args, rc, stdout='', stderr=secret)
        assert args == ['systemctl', 'is-active', '--quiet', 'klipper']
        return subprocess.CompletedProcess(args, 0 if active else 3)
    def call(args, **kwargs):
        calls.append((args, kwargs))
        assert args[0] == original['python']
        assert args[args.index('-b')+1] == '250000'
        assert kwargs['stdin'] == subprocess.DEVNULL
        assert '-r' not in args
        assert ('-s' in args) == check
        if returned and not raises:
            pathlib.Path('application-ready').write_text('COMPLETE acknowledged')
        if raises:
            raise OSError('simulated')
        pathlib.Path('operation.log').write_text(('Status Request Complete' if check else 'Programming Complete') if marker else '')
        return flash
    capture = io.StringIO()
    with tempfile.TemporaryDirectory() as directory:
        previous = os.getcwd()
        try:
            os.chdir(directory)
            with patch.object(subprocess, 'run', run), patch.object(subprocess, 'call', call), patch.object(time, 'sleep'), contextlib.redirect_stdout(capture):
                code = space['run_job'](secret)
        finally:
            os.chdir(previous)
    assert secret not in capture.getvalue()
    actions = [a[6] for a, _ in calls if a[0] == 'sudo']
    flashes = [a for a, _ in calls if a[0] == original['python'] and '-f' in a]
    requests = [a for a, _ in calls if a[0] == original['python'] and '-r' in a]
    assert not requests, 'Runner must not close/reopen UART via a separate -r process'
    results[name] = {'code': code, 'service_actions': actions, 'firmware_invocations': len(flashes), 'uart_requests': len(requests)}
    return code, actions, flashes

code, actions, flashes = scenario('success')
assert code == 0 and actions == ['stop', 'start'] and len(flashes) == 1
code, actions, flashes = scenario('sudo_denied', stop=1)
assert code == 90 and actions == ['stop'] and not flashes
code, actions, flashes = scenario('initially_inactive', active=False)
assert code == 0 and actions == [] and len(flashes) == 1
code, actions, flashes = scenario('option_off', enabled=False)
assert code == 0 and actions == [] and len(flashes) == 1
code, actions, flashes = scenario('firmware_failure', flash=5)
assert code == 5 and actions == ['stop', 'start']
code, actions, flashes = scenario('firmware_exception', raises=True)
assert code == 93 and actions == ['stop', 'start']
code, actions, flashes = scenario('restore_denied', start=1)
assert code == 91 and actions == ['stop', 'start']
code, actions, flashes = scenario('completion_marker_missing', marker=False)
assert code == 92 and actions == ['stop', 'start']
code, actions, flashes = scenario('ch340_check')
assert code == 0 and actions == ['stop', 'start'] and len(flashes) == 1
code, actions, flashes = scenario('ch340_flash', check=False)
assert code == 0 and actions == ['stop', 'start'] and len(flashes) == 1
code, actions, flashes = scenario('connect_failure_restored', flash=1, returned=False)
assert code == 1 and actions == ['stop', 'start'] and len(flashes) == 1
code, actions, flashes = scenario('missing_return_marker', returned=False)
assert code == 95 and actions == ['stop', 'start']
code, actions, flashes = scenario('ch340_auth_failure', stop=1)
assert code == 90 and actions == ['stop'] and not flashes
report = {'success': True, 'scenarios': results, 'credential_absent_from_script_commands_logs': True,
          'real_linux_sudo_tested': False, 'physical_hardware_opened': False}
pathlib.Path(sys.argv[1]).with_name('report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print(json.dumps(report, indent=2))
