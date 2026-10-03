using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Renci.SshNet;

namespace KlipperWinForms {
    // Runs on the Linux host using its already selected Python interpreter.
    // The credential is sent through SSH stdin, never placed in a script or command line.
    static class SshServiceRunner {
        internal static bool NeedsSerialRequest(Dictionary<string,object> device) {
            int vendor, product;
            return int.TryParse(Json.S(device,"vid"),System.Globalization.NumberStyles.HexNumber,System.Globalization.CultureInfo.InvariantCulture,out vendor)
                && int.TryParse(Json.S(device,"pid"),System.Globalization.NumberStyles.HexNumber,System.Globalization.CultureInfo.InvariantCulture,out product)
                && SerialFlash.IsCh34x(new Dictionary<string,object>{{"vid",vendor},{"pid",product}});
        }
        internal static string Script(string directory, string python, string device, string service, bool check, bool stop) {
            if(!Regex.IsMatch(service, @"^[A-Za-z0-9_][A-Za-z0-9_.@-]*$")) throw new System.Exception("Invalid service name");
            var config = new Dictionary<string,object> {
                {"directory",directory}, {"python",python}, {"device",device},
                {"service",service}, {"check",check}, {"stop",stop}
            };
            return "import json, os, subprocess, sys, time\ncfg = json.loads(" + U.CString(Json.Write(config)) + ")\n" + @"
def service_action(action, password):
    result = subprocess.run(['sudo', '-S', '-p', '', '--', 'systemctl', action, cfg['service']],
                            input=password + '\n', stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                            universal_newlines=True, timeout=45)
    if result.returncode:
        print('Klipper service ' + action + ' failed: sudo authentication or permission denied.', flush=True)
        # Do not write captured authentication output or credentials to logs.
    elif action == 'start':
        state = subprocess.run(['systemctl', 'is-active', '--quiet', cfg['service']], timeout=30)
        if state.returncode:
            print('Klipper service start returned, but the service is not active.', flush=True)
            return state.returncode
    return result.returncode

def run_job(password):
    code, restore, launched = 1, False, False
    cfg['service_restarted'] = False
    try:
        if cfg['stop']:
            state = subprocess.run(['systemctl', 'is-active', '--quiet', cfg['service']], timeout=30)
            if state.returncode == 0:
                if service_action('stop', password) != 0:
                    return 90
                restore = True
                print('Klipper service stopped.', flush=True)
        base = [cfg['python'], '-B', '-u', 'flashtool.py', '-d', cfg['device'], '-b', '250000']
        command = base + ['-f', 'klipper.bin']
        if cfg['check']:
            command.append('-s')
        launched = True
        code = subprocess.call(command, stdin=subprocess.DEVNULL)
        if code == 0:
            marker = 'Status Request Complete' if cfg['check'] else 'Programming Complete'
            with open('operation.log', encoding='utf-8', errors='replace') as log:
                if marker not in log.read():
                    code = 92
    except Exception as error:
        print('Remote operation failed: ' + type(error).__name__, flush=True)
        code = 93
    finally:
        if launched and not os.path.isfile('application-ready'):
            if code == 0:
                code = 95
            if restore:
                print('Application return was not acknowledged. Restarting the service stopped by this job.', flush=True)
        if restore:
            try:
                if service_action('start', password) != 0:
                    code = 91
                else:
                    cfg['service_restarted'] = True
                    print('Klipper service restarted (active).', flush=True)
            except Exception:
                print('Klipper service restore failed.', flush=True)
                code = 91
    return code

def main():
    password = json.loads(sys.stdin.readline())
    if '\n' in password or '\r' in password:
        raise ValueError('Unsupported password line break')
    os.umask(0o077)
    os.chdir(cfg['directory'])
    child = os.fork()
    if child:
        print(child, flush=True)
        return
    os.setsid()
    with open(os.devnull, 'rb') as source, open('operation.log', 'ab', buffering=0) as log:
        os.dup2(source.fileno(), 0)
        os.dup2(log.fileno(), 1)
        os.dup2(log.fileno(), 2)
    code = run_job(password)
    password = None
    with open('result.partial', 'w') as result:
        json.dump({'exit_code': code, 'service_restarted': cfg.get('service_restarted', False)}, result)
    os.replace('result.partial', 'result.json')
    os._exit(0)

if __name__ == '__main__':
    main()
";
        }

        internal static string Start(SshClient client, string python, string path, string password) {
            using(var command = client.CreateCommand(U.Shell(python) + " -B -u " + U.Shell(path))) {
                command.CommandTimeout = System.TimeSpan.FromSeconds(40);
                var pending = command.BeginExecute();
                byte[] input = Encoding.UTF8.GetBytes(Json.Write(password) + "\n");
                try { using(var stream = command.CreateInputStream()) stream.Write(input,0,input.Length); }
                finally { System.Array.Clear(input,0,input.Length); }
                string pid = command.EndExecute(pending).Trim();
                if(command.ExitStatus != 0 || !Regex.IsMatch(pid,@"^[1-9][0-9]*$"))
                    throw new System.Exception("원격 작업 시작을 확인하지 못했습니다. 재시도 전에 원격 operation.log/result.json을 확인하세요: " + path);
                return pid;
            }
        }
    }
}
