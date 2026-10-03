# Executed only by the remote Linux Python. No Windows Python dependency.
import os, sys, json, pathlib, subprocess, time, hashlib, struct, re, stat

class BootselNotReady(RuntimeError):pass

def text(path):
    try: return pathlib.Path(path).read_text().strip()
    except OSError: return ''

def usb_info(path):
    path = pathlib.Path(path).resolve()
    return dict(usb_path=str(path), vid=text(path/'idVendor').lower(), pid=text(path/'idProduct').lower(), serial=text(path/'serial'))

def device_usb(device):
    tty = pathlib.Path(device).resolve().name
    path = pathlib.Path('/sys/class/tty', tty).resolve()
    for parent in (path,) + tuple(path.parents):
        if (parent/'idVendor').is_file(): return usb_info(parent)
    raise RuntimeError('Cannot identify selected USB serial device')

def ensure_free(device):
    wanted=os.stat(device)
    deadline=time.monotonic()+4
    for path in pathlib.Path('/proc').glob('[0-9]*/fd/*'):
        if time.monotonic()>deadline:raise TimeoutError('Serial ownership check exceeded 4 seconds')
        try:
            actual=path.stat()
            if (actual.st_dev,actual.st_ino)==(wanted.st_dev,wanted.st_ino):
                raise RuntimeError('Serial device is in use by process '+path.parts[2]+'. Enable Klipper service stop.')
        except OSError:pass

def enter_bootloader(selected):
    import fcntl,termios,errno
    print('Checking selected USB identity and serial ownership.',flush=True)
    same(selected,device_usb(selected['device']));ensure_free(selected['device'])
    print('Opening selected serial port with O_NONBLOCK | O_NOCTTY.',flush=True)
    fd=os.open(selected['device'],os.O_RDWR|os.O_NOCTTY|os.O_NONBLOCK)
    try:
        fcntl.flock(fd,fcntl.LOCK_EX|fcntl.LOCK_NB)
        actual=os.fstat(fd);expected=os.stat(selected['device'])
        if (actual.st_dev,actual.st_ino)!=(expected.st_dev,expected.st_ino):raise RuntimeError('Serial device changed while opening')
        same(selected,device_usb(selected['device']))
        print('Applying 1200 baud and DTR transition.',flush=True)
        fcntl.ioctl(fd,termios.TIOCMBIS,struct.pack('I',termios.TIOCM_DTR))
        attrs=termios.tcgetattr(fd);attrs[2]|=termios.CLOCAL|termios.CREAD;attrs[4]=attrs[5]=termios.B1200
        termios.tcsetattr(fd,termios.TCSANOW,attrs)
        try:fcntl.ioctl(fd,termios.TIOCMBIC,struct.pack('I',termios.TIOCM_DTR))
        except OSError as error:
            if error.errno not in (errno.EIO,errno.ENODEV):raise
            print('USB disconnected during DTR transition; verify the new bootloader device.',flush=True)
    finally:os.close(fd)

def request_bootloader(selected):
    # This child can only inspect/open/reboot the selected serial device; never write firmware.
    print('1200 baud preflight/entry started (12 second deadline).',flush=True)
    child=subprocess.Popen([sys.executable,'-B',str(pathlib.Path(__file__).resolve()),'--enter-bootloader',json.dumps(selected)],stdin=subprocess.DEVNULL,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
    try:
        output,_=child.communicate(timeout=12)
    except subprocess.TimeoutExpired as error:
        try:child.kill()
        except ProcessLookupError:pass
        try:output,_=child.communicate(timeout=2)
        except subprocess.TimeoutExpired:
            output=error.output or b''
            if child.stdout:child.stdout.close()
            print('Reboot helper PID '+str(child.pid)+' did not exit promptly; no firmware transfer will start.',flush=True)
        if output:print(output.decode(errors='replace').rstrip(),flush=True)
        raise TimeoutError('1200 baud preflight/entry timed out; UF2 was not written')
    if output:print(output.decode(errors='replace').rstrip(),flush=True)
    if child.returncode:raise RuntimeError('1200 baud entry failed; UF2 was not written')

def scan_bootsel():
    found = []
    for path in pathlib.Path('/sys/bus/usb/devices').glob('*'):
        item = usb_info(path)
        if item['vid'] == '2e8a' and item['pid'] in ('0003','000f'):
            mcu='rp2040' if item['pid']=='0003' else 'rp2350'
            item.update(kind='bootsel', mcu=mcu, device=item['usb_path'], realpath=item['usb_path'], product=mcu.upper()+' BOOTSEL', manufacturer='Raspberry Pi', readable=True, writable=True, aliases=[])
            found.append(item)
    return found

def same(expected, actual):
    for key in ('usb_path','vid','pid','serial'):
        if expected.get(key) and expected[key] != actual.get(key): raise RuntimeError('Selected device identity changed: ' + key)

def block_for(usb_path):
    usb = pathlib.Path(usb_path)
    candidates = [p for p in pathlib.Path('/sys/class/block').glob('*') if usb in p.resolve().parents]
    partitions = [p for p in candidates if (p/'partition').exists()]
    choices = partitions or candidates
    if not choices:raise BootselNotReady('BOOTSEL block device not present yet')
    if len(choices) != 1: raise RuntimeError('BOOTSEL block device ambiguous')
    return '/dev/' + choices[0].name

def usb_epoch(target):
    root=pathlib.Path(target['usb_path']);bus=text(root/'busnum');device=text(root/'devnum')
    if not bus or not device:raise BootselNotReady('USB enumeration is incomplete')
    return (bus,device)

def block_signature(device):
    item=os.stat(device)
    if not stat.S_ISBLK(item.st_mode):raise RuntimeError('Selected storage node is not a block device')
    return (item.st_rdev,item.st_ino)

def checked_bootsel(target):
    actual=usb_info(target['usb_path'])
    if not actual['vid'] or not actual['pid']:raise BootselNotReady('BOOTSEL USB temporarily unavailable')
    same(target,actual)

def wait_storage(target,board_id,password,prepared):
    print('BOOTSEL detected. Waiting at least 5 seconds for USB, partition and mount readiness (45 second limit).',flush=True)
    begin=time.monotonic();stable=None;since=begin;last_log=begin;device_stable=None;device_since=begin
    while time.monotonic()-begin<45:
        now=time.monotonic()
        try:
            checked_bootsel(target)
            epoch=usb_epoch(target);device=block_for(target['usb_path']);signature=block_signature(device)
            device_key=(epoch,device,signature)
            if device_key!=device_stable:device_stable=device_key;device_since=now;stable=None
            mountpoint=existing_mount(device)
            if not mountpoint:
                if prepared.get('mounted'):raise RuntimeError('Installer-owned BOOTSEL mount disappeared or changed; no firmware written')
                if now-begin<5 or now-device_since<2:raise BootselNotReady('Waiting for stable partition before mounting')
                # Allow an automounter to win the race; do not repeat sudo on auth failures.
                mountpoint=str(pathlib.Path('mount').resolve());pathlib.Path(mountpoint).mkdir(exist_ok=True)
                try:
                    sudo(['mount','-t','vfat','-o','rw,nosuid,nodev,noexec,uid='+str(os.getuid())+',gid='+str(os.getgid())+',umask=077',device,mountpoint],password)
                    prepared.update(mountpoint=mountpoint,mounted=True)
                except Exception:
                    mountpoint=existing_mount(device)
                    if not mountpoint:raise
                stable=None
            checked_bootsel(target)
            if usb_epoch(target)!=epoch or block_for(target['usb_path'])!=device or block_signature(device)!=signature or existing_mount(device)!=mountpoint:
                raise BootselNotReady('USB/partition/mount is still changing')
            info=pathlib.Path(mountpoint,'INFO_UF2.TXT')
            if info.stat().st_size>8192:raise RuntimeError('Invalid INFO_UF2 size')
            ids=[line.strip() for line in info.read_text().splitlines() if line.strip().startswith('Board-ID:')]
            if not ids:raise BootselNotReady('Waiting for INFO_UF2 Board-ID')
            if ids!=['Board-ID: '+board_id]:raise RuntimeError('UF2 volume Board-ID does not match MCU')
            if not os.access(mountpoint,os.W_OK):raise BootselNotReady('Waiting for writable BOOTSEL volume')
            key=(device_key,mountpoint)
            if key!=stable:stable=key;since=now
            if now-begin>=5 and now-since>=2:
                print('BOOTSEL storage ready: '+device+' -> '+mountpoint+'. Starting a single firmware transfer.',flush=True)
                prepared.update(device=device,mountpoint=mountpoint,signature=signature,epoch=epoch)
                return
        except (BootselNotReady,FileNotFoundError,PermissionError):stable=None
        except OSError as error:
            import errno
            if error.errno not in (errno.EIO,errno.ENODEV,errno.ENXIO,errno.EBUSY):raise
            stable=None
        if now-last_log>=5:print('Still waiting for stable BOOTSEL storage; no firmware bytes sent.',flush=True);last_log=now
        time.sleep(1)
    raise TimeoutError('BOOTSEL storage did not become stable/readable/writable within 45 seconds; no firmware bytes sent')

def unescape_mount(value):
    return re.sub(r'\\([0-7]{3})', lambda m: chr(int(m.group(1),8)), value)

def existing_mount(device):
    dev = os.stat(device).st_rdev
    number = str(os.major(dev)) + ':' + str(os.minor(dev))
    matches = []
    for line in pathlib.Path('/proc/self/mountinfo').read_text().splitlines():
        fields = line.split()
        if fields[2] == number and fields[3] == '/': matches.append(unescape_mount(fields[4]))
    if len(matches) > 1: raise RuntimeError('Multiple BOOTSEL mounts')
    return matches[0] if matches else None

def sudo(arguments, password):
    p = subprocess.run(['sudo','-S','-p','','--'] + arguments, input=password+'\n', stdout=subprocess.PIPE, stderr=subprocess.PIPE, universal_newlines=True, timeout=45)
    if p.returncode: raise RuntimeError('sudo failed for ' + arguments[0] + '; check password and permissions')

def restart_service(service,password):
    sudo(['systemctl','start',service],password)
    if subprocess.run(['systemctl','is-active','--quiet',service],timeout=30).returncode:
        raise RuntimeError('Service start returned but the service is not active')

def crc(data):
    value = 0xffff
    for b in data:
        b ^= value & 255; b ^= (b & 15) << 4
        value = (((b << 8) | (value >> 8)) ^ (b >> 4) ^ (b << 3)) & 65535
    return value

def verify_katapult(usb_path, app, mcu='rp2040'):
    import serial
    ports = [p for p in pathlib.Path('/sys/class/tty').glob('*') if pathlib.Path(usb_path) in p.resolve().parents]
    if len(ports) != 1: return False
    frame = b'\x01\x88\x11\x00'
    frame += struct.pack('<H',crc(frame[2:])) + b'\x99\x03'
    with serial.Serial('/dev/'+ports[0].name,250000,timeout=0.25,write_timeout=3,exclusive=True) as port:
        for attempt in range(3):
            port.write(frame); port.flush(); data=b''; end=time.monotonic()+2
            while time.monotonic()<end:
                data += port.read(256)
                while len(data)>=4:
                    if data[:2]!=b'\x01\x88': data=data[1:]; continue
                    length=data[3]*4+8
                    if len(data)<length: break
                    response,data=data[:length],data[length:]
                    if length<12 or response[-2:]!=b'\x99\x03' or crc(response[2:-4])!=struct.unpack('<H',response[-4:-2])[0]: continue
                    if response[2]!=0xa0 or struct.unpack('<I',response[4:8])[0]!=17: continue
                    payload=response[8:-4]
                    if len(payload)<13: continue
                    if payload[12:].split(b'\0',1)[0]!=mcu.encode() or struct.unpack('<I',payload[4:8])[0]!=app:
                        raise RuntimeError('Katapult MCU/application address mismatch after installation')
                    return True
    return False

def install(password):
    selected=cfg['selected']; stopped=False; wrote=False; transferred=False; mounted=False; mountpoint=None;prepared={}
    mcu=cfg.get('mcu','rp2040')
    full=bool(cfg.get('replace_bootloader',False))
    manual=full and (selected.get('vid'),selected.get('pid'))==('1d50','6177')
    if mcu not in ('rp2040','rp2350'):raise RuntimeError('Unsupported RP MCU')
    expected_pid='0003' if mcu=='rp2040' else '000f'
    board_id='RPI-RP2' if mcu=='rp2040' else 'RP2350'
    result=dict(exit_code=1, transferred=False, katapult_connected=False, application_usb_detected=False, service_left_stopped=False)
    try:
        image=pathlib.Path('katapult.uf2').read_bytes()
        if hashlib.sha256(image).hexdigest()!=cfg['sha256']: raise RuntimeError('Staged UF2 hash mismatch')
        if selected.get('kind')=='bootsel':
            actual=usb_info(selected['usb_path']); same(selected,actual)
            if (actual['vid'],actual['pid'])!=('2e8a',expected_pid): raise RuntimeError('BOOTSEL MCU does not match UF2; no write')
        else:
            actual=device_usb(selected['device']); same(selected,actual)
            if (actual['vid'],actual['pid'])!=('1d50','614e') and not manual: raise RuntimeError('Select native Klipper USB or RP2040/RP2350 BOOTSEL; CH340 is unsupported')
        if cfg['stop'] and subprocess.run(['systemctl','is-active','--quiet',cfg['service']],timeout=30).returncode==0:
            sudo(['systemctl','stop',cfg['service']],password); stopped=True
            print('Klipper service stopped.',flush=True)
        if manual:
            print('Katapult cannot automatically enter BOOTSEL. Hold BOOTSEL and reconnect USB at the SAME physical port. Waiting up to 120 seconds; no serial request or firmware write.',flush=True)
        elif selected.get('kind')!='bootsel':
            request_bootloader(selected)
            print('1200 baud request sent; waiting for RP2040/RP2350 BOOTSEL at the same USB location.',flush=True)
        target=None; deadline=time.monotonic()+(120 if manual else 30);katapult_since=None;last_log=time.monotonic()
        while time.monotonic()<deadline:
            matches=[p for p in scan_bootsel() if p['usb_path']==selected['usb_path']]
            if len(matches)>1: raise RuntimeError('Ambiguous BOOTSEL target')
            if matches: target=matches[0];break
            state=usb_info(selected['usb_path'])
            if not manual and (state['vid'],state['pid'])==('1d50','6177'):
                if katapult_since is None:katapult_since=time.monotonic()
                elif time.monotonic()-katapult_since>=5:raise RuntimeError('Existing Katapult remains active; enter BOOTSEL manually. No UF2 written.')
            else:katapult_since=None
            if manual and time.monotonic()-last_log>=10:print('Waiting for manual BOOTSEL entry at the selected USB location; no firmware written.',flush=True);last_log=time.monotonic()
            time.sleep(0.5)
        if not target: raise RuntimeError('RP2040/RP2350 BOOTSEL not found at selected USB location; no UF2 written')
        if target['pid']!=expected_pid:raise RuntimeError('BOOTSEL MCU does not match UF2; no write')
        wait_storage(target,board_id,password,prepared);device=prepared['device'];mountpoint=prepared['mountpoint'];mounted=prepared.get('mounted',False)
        checked_bootsel(target)
        if usb_epoch(target)!=prepared['epoch'] or block_for(target['usb_path'])!=device or block_signature(device)!=prepared['signature'] or existing_mount(device)!=mountpoint:raise RuntimeError('Storage changed immediately before transfer; no firmware written')
        print('Transferring '+('full Klipper' if full else 'Katapult')+' UF2 to '+device+' at '+target['usb_path'],flush=True)
        wrote=True
        with open(pathlib.Path(mountpoint,'katapult.uf2'),'wb',buffering=0) as stream:
            if stream.write(image)!=len(image): raise RuntimeError('Incomplete UF2 write')
            os.fsync(stream.fileno())
        transferred=True;result['transferred']=True
        print('Klipper UF2 transfer complete. Firmware start address: 0x10000000.' if full else 'Katapult UF2 transfer complete. Install Klipper firmware with the matching offset.',flush=True)
        if cfg['communication']=='USB':
            deadline=time.monotonic()+25
            while time.monotonic()<deadline:
                state=usb_info(target['usb_path'])
                if full and (state['vid'],state['pid'])==('1d50','614e'):
                    result['application_usb_detected']=True;break
                if not full and (state['vid'],state['pid'])==('1d50','6177'):
                    time.sleep(1)
                    if verify_katapult(target['usb_path'],cfg['application_start'],mcu):result['katapult_connected']=True;break
                time.sleep(0.5)
        if full:print('Klipper USB detected (not a full runtime/readback verification).' if result['application_usb_detected'] else 'UF2 transferred; Klipper USB not confirmed. Check board state.',flush=True)
        else:print('Katapult MCU/address confirmed.' if result['katapult_connected'] else 'UF2 transferred; Katapult execution not confirmed. Check interface settings and board.',flush=True)
        result['exit_code']=0
    except Exception as error:
        print('Firmware installation failed: '+str(error),flush=True)
        if wrote and not transferred: print('Write outcome uncertain; no automatic retry. Check board state.',flush=True)
    finally:
        if prepared.get('mounted'):mounted=True;mountpoint=prepared['mountpoint']
        if mounted:
            try:sudo(['umount',mountpoint],password)
            except Exception:print('Temporary BOOTSEL mount could not be unmounted: '+mountpoint,flush=True)
        if stopped:
            try:
                restart_service(cfg['service'],password);result['service_restarted']=True
                print('Klipper service restarted (active).',flush=True)
            except Exception as error:
                result['service_left_stopped']=True;result['service_restore_failed']=True;result['exit_code']=1
                print('Klipper service restart failed: '+str(error),flush=True)
    return result

def main():
    if len(sys.argv)>1 and sys.argv[1]=='--enter-bootloader':
        try:enter_bootloader(json.loads(sys.argv[2]))
        except Exception as error:print('Bootloader entry failed: '+str(error),flush=True);sys.exit(1)
        return
    if '--scan' in sys.argv:
        print(json.dumps(scan_bootsel()));return
    password=json.loads(sys.stdin.readline())
    if '\n' in password or '\r' in password:raise ValueError('Invalid password line break')
    os.umask(0o077);os.chdir(cfg['directory'])
    child=os.fork()
    if child:print(child,flush=True);return
    os.setsid()
    with open(os.devnull,'rb') as null,open('operation.log','ab',buffering=0) as log:
        os.dup2(null.fileno(),0);os.dup2(log.fileno(),1);os.dup2(log.fileno(),2)
    result=install(password);password=None
    pathlib.Path('result.partial').write_text(json.dumps(result));os.replace('result.partial','result.json');os._exit(0)

if __name__ == '__main__':
    main()
