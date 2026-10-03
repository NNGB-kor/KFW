# Linux USB control bridge. Firmware parsing and DFU state machine stay in C#.
import os, sys, json, pathlib, time, socket, array, subprocess, base64, ctypes
import common

def emit(value):print(json.dumps(value),flush=True)
def log(message):emit({'event':message})
def scan():
    result=[]
    for path in pathlib.Path('/sys/bus/usb/devices').glob('*'):
        item=common.usb_info(path)
        if (item['vid'],item['pid'])==('0483','df11'):
            item.update(kind='dfu',device=item['usb_path'],realpath=item['usb_path'],product='STM32 DFU',manufacturer='STMicroelectronics',aliases=[],readable=True,writable=True)
            result.append(item)
    return result

def node_for(expected):
    common.same(expected,common.usb_info(expected['usb_path']))
    root=pathlib.Path(expected['usb_path'])
    bus=int(common.text(root/'busnum'));dev=int(common.text(root/'devnum'))
    return '/dev/bus/usb/%03d/%03d'%(bus,dev)

def send_open_fd():
    expected=json.loads(sys.argv[3]);node=node_for(expected)
    fd=os.open(node,os.O_RDWR)
    try:
        with socket.socket(socket.AF_UNIX,socket.SOCK_DGRAM) as channel:
            channel.connect(sys.argv[2]);channel.sendmsg([b'F'],[(socket.SOL_SOCKET,socket.SCM_RIGHTS,array.array('i',[fd]))])
    finally:os.close(fd)

def open_fd(expected,password,python):
    node=node_for(expected)
    try:return os.open(node,os.O_RDWR)
    except PermissionError:pass
    path=str(pathlib.Path('usb-fd.sock').resolve())
    with socket.socket(socket.AF_UNIX,socket.SOCK_DGRAM) as channel:
        channel.bind(path);os.chmod(path,0o600);channel.settimeout(3)
        try:
            common.sudo([python,'-B',str(pathlib.Path(__file__).resolve()),'--open',path,json.dumps(expected)],password)
            data,ancillary,flags,addr=channel.recvmsg(1,socket.CMSG_SPACE(array.array('i').itemsize))
            for level,kind,value in ancillary:
                if level==socket.SOL_SOCKET and kind==socket.SCM_RIGHTS:
                    values=array.array('i');values.frombytes(value[:values.itemsize]);return values[0]
            raise RuntimeError('Privileged USB open returned no descriptor')
        finally:os.unlink(path)

class Control(ctypes.Structure):
    _fields_=[('type',ctypes.c_ubyte),('request',ctypes.c_ubyte),('value',ctypes.c_ushort),('index',ctypes.c_ushort),('length',ctypes.c_ushort),('timeout',ctypes.c_uint),('data',ctypes.c_void_p)]
def ioctl(fd,request,obj):
    libc=ctypes.CDLL(None,use_errno=True);fn=libc.ioctl;fn.argtypes=[ctypes.c_int,ctypes.c_ulong,ctypes.c_void_p];fn.restype=ctypes.c_int
    result=fn(fd,request,ctypes.byref(obj))
    if result<0:raise OSError(ctypes.get_errno(),'USB ioctl failed')
    return result

def main():
    if len(sys.argv)>1 and sys.argv[1]=='--open':send_open_fd();return
    request=json.loads(sys.stdin.readline());password=request.pop('password');selected=request['selected'];stopped=False;fd=None;claimed=None
    if '\n' in password or '\r' in password:raise RuntimeError('Invalid password line break')
    os.umask(0o077);os.chdir(pathlib.Path(__file__).resolve().parent)
    try:
        if selected.get('kind')=='dfu':
            common.same(selected,common.usb_info(selected['usb_path']))
            if (selected['vid'],selected['pid'])!=('0483','df11'):raise RuntimeError('Not STM32 DFU')
        else:
            common.same(selected,common.device_usb(selected['device']))
            if (selected['vid'],selected['pid'])!=('1d50','614e'):raise RuntimeError('Select native Klipper USB or STM32 DFU')
        if request['stop'] and subprocess.run(['systemctl','is-active','--quiet',request['service']],timeout=30).returncode==0:
            common.sudo(['systemctl','stop',request['service']],password);stopped=True;log('Klipper service stopped.')
        if selected.get('kind')!='dfu':
            # Keep stdout machine-readable: helper diagnostics become JSON events.
            import contextlib,io
            diagnostics=io.StringIO();log('1200 baud preflight/entry started (12 second deadline).')
            try:
                with contextlib.redirect_stdout(diagnostics):common.request_bootloader(selected)
            finally:
                for line in diagnostics.getvalue().splitlines():log(line)
            log('1200 baud/DTR sent; waiting for STM32 DFU at the same USB location.')
        target=None;end=time.monotonic()+30
        while time.monotonic()<end:
            matches=[p for p in scan() if p['usb_path']==selected['usb_path']]
            if len(matches)>1:raise RuntimeError('Ambiguous DFU location')
            if matches:target=matches[0];break
            state=common.usb_info(selected['usb_path'])
            if (state['vid'],state['pid'])==('1d50','6177'):raise RuntimeError('Entered existing Katapult; enter ROM DFU manually. No writes performed.')
            time.sleep(0.5)
        if target is None:raise RuntimeError('STM32 DFU not found at selected USB location')
        node=node_for(target);fd=open_fd(target,password,request['python'])
        emit({'ready':True,'device':target})
        for line in sys.stdin:
            message=json.loads(line)
            if message['op']=='close':emit({'ok':True});break
            if node_for(target)!=node:raise RuntimeError('USB device changed')
            if message['op']=='select':
                number=int(message['interface']);alt=int(message['alternate'])
                if not 0<=number<=255 or not 0<=alt<=255:raise RuntimeError('Bad USB interface')
                if claimed is None:ioctl(fd,0x8004550f,ctypes.c_uint(number));claimed=number
                if number!=claimed:raise RuntimeError('Interface changed')
                values=(ctypes.c_uint*2)(number,alt);ioctl(fd,0x80085504,values);emit({'ok':True});continue
            if message['op']!='control':raise RuntimeError('Unknown operation')
            kind=int(message['type']);cmd=int(message['request']);value=int(message['value']);index=int(message['index']);size=int(message['length'])
            if kind not in (0x80,0xa1,0x21) or not 0<=cmd<=255 or not 0<=value<=65535 or not 0<=index<=65535 or not 0<=size<=16384:raise RuntimeError('Invalid control request')
            payload=base64.b64decode(message['data'],validate=True)
            if kind&128 and payload:raise RuntimeError('Input transfer must not contain an output payload')
            if not kind&128 and len(payload)!=size:raise RuntimeError('USB payload length mismatch')
            buffer=ctypes.create_string_buffer(max(size,1))
            if payload:ctypes.memmove(buffer,payload,len(payload))
            control=Control(kind,cmd,value,index,size,5000,ctypes.addressof(buffer))
            count=ioctl(fd,0xc0005500|(ctypes.sizeof(Control)<<16),control)
            if count>size or (not kind&128 and count!=size):raise RuntimeError('USB short transfer')
            emit({'ok':True,'data':base64.b64encode(buffer.raw[:count]).decode()})
    except Exception as error:emit({'error':str(error)})
    finally:
        restored=False;restore_error=False
        try:
            if fd is not None:
                if claimed is not None:
                    try:ioctl(fd,0x80045510,ctypes.c_uint(claimed))
                    except OSError:pass
                try:os.close(fd)
                except OSError:pass
        finally:
            if stopped:
                try:common.restart_service(request['service'],password);restored=True
                except Exception:restore_error=True
            password=None
            if stopped:log('Klipper service restarted (active).' if restored else 'Klipper service restart failed. Check systemd/sudo permissions.')
            emit({'cleanup_complete':True,'service_stopped':stopped,'service_restarted':restored,'service_restore_failed':restore_error})

if __name__=='__main__':main()
