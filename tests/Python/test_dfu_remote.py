"""Linux USB bridge state/identity/control framing tests with no OS USB or sudo."""
import base64,contextlib,ctypes,io,json,pathlib,subprocess,sys,types
from unittest.mock import patch
root=pathlib.Path(sys.argv[1]).resolve();source=(root/'dfu_usb.py').read_text(encoding='utf-8')
reports={}
target=dict(kind='dfu',device='/sys/devices/usb1/1-5',usb_path='/sys/devices/usb1/1-5',vid='0483',pid='df11',serial='test-uid')
def scenario(name,change=False,short=False,wrong_payload=False,native=False,active=True,enabled=True,restore_error=False,entry_error=False,stop_error=False,close_error=False):
    calls=[];events=[];opens=[];fdClosed=[];selected=dict(target)
    if native:selected.update(kind='serial',vid='1d50',pid='614e',device='/dev/serial/by-id/test-klipper')
    def same(expected,actual):
        if any(expected.get(k) and expected[k]!=actual.get(k) for k in ('usb_path','vid','pid','serial')):raise RuntimeError('USB identity changed')
    def entry(device):
        assert device==selected;calls.append(['1200-entry']);print('Simulated bounded entry diagnostics')
        if entry_error:raise RuntimeError('entry failed')
    def sudo(args,pw):
        assert pw=='fake-private-password';calls.append(args)
        if stop_error:raise RuntimeError('stop denied')
    def restart(service,pw):
        assert pw=='fake-private-password';calls.append(['systemctl','start',service])
        if restore_error:raise RuntimeError('start denied')
    common=types.SimpleNamespace(usb_info=lambda p:dict(target),device_usb=lambda p:dict(selected),request_bootloader=entry,same=same,sudo=sudo,restart_service=restart,text=lambda p:'1')
    scope={'__name__':'bridge_test','__file__':str(root/'dfu_usb.py')}
    with patch.dict(sys.modules,{'common':common}):exec(compile(source,'dfu_usb.py','exec'),scope)
    data=base64.b64encode(b'abc').decode() if wrong_payload else ''
    request=dict(password='fake-private-password',selected=selected,python='/usr/bin/python3',stop=enabled,service='klipper')
    commands=[dict(op='select',interface=0,alternate=0),dict(op='control',type=0x80,request=6,value=256,index=0,data='',length=18),dict(op='control',type=0x21,request=1,value=0,index=0,data=data,length=5 if wrong_payload else 0),dict(op='close')]
    stdin=io.StringIO('\n'.join(json.dumps(x) for x in [request]+commands)+'\n');out=io.StringIO();counter=[0]
    def node(expected):
        counter[0]+=1
        return '/dev/bus/usb/001/009' if change and counter[0]>1 else '/dev/bus/usb/001/005'
    def ioctl(fd,code,obj):
        assert fd==77;calls.append(['ioctl',code])
        if isinstance(obj,scope['Control']):
            if obj.type==0x80:
                ctypes.memmove(obj.data,bytes(range(18)),18);return 18
            return 1 if short else obj.length
        return 0
    with contextlib.ExitStack() as stack:
        stack.enter_context(patch.dict(scope,scan=lambda:[dict(target)],node_for=node,open_fd=lambda *a:77,ioctl=ioctl))
        stack.enter_context(patch.object(sys,'stdin',stdin));stack.enter_context(patch.object(sys,'argv',['dfu_usb.py']))
        stack.enter_context(patch.object(scope['os'],'chdir',lambda *a:None));stack.enter_context(patch.object(scope['os'],'umask',lambda *a:0))
        def close(fd):
            fdClosed.append(fd)
            if close_error:raise OSError('close error')
        stack.enter_context(patch.object(scope['os'],'close',close))
        stack.enter_context(patch.object(subprocess,'run',lambda *a,**kw:subprocess.CompletedProcess(a,0 if active else 3)))
        stack.enter_context(contextlib.redirect_stdout(out));scope['main']()
    output=out.getvalue();assert 'fake-private-password' not in output
    events=[json.loads(line) for line in output.splitlines()]
    assert fdClosed==([] if entry_error or stop_error else [77])
    stopped=active and enabled and not stop_error
    assert (['systemctl','start','klipper'] in calls)==stopped
    cleanup=events[-1];assert cleanup['cleanup_complete'] and cleanup['service_restarted']==(stopped and not restore_error) and cleanup['service_restore_failed']==(stopped and restore_error)
    errors=[x['error'] for x in events if 'error' in x]
    if change or short or wrong_payload or entry_error or stop_error:assert errors
    else:assert not errors and any(x.get('data')==base64.b64encode(bytes(range(18))).decode() for x in events)
    if change:assert not any(isinstance(x,list) and x[0]=='ioctl' for x in calls)
    if native:assert ['1200-entry'] in calls and any('Simulated bounded' in x.get('event','') for x in events)
    reports[name]=dict(events=events,ioctl_calls=len([x for x in calls if x[0]=='ioctl']))
scenario('normal')
scenario('native_entry_preserves_json_channel',native=True)
scenario('device_replaced',change=True)
scenario('short_transfer',short=True)
scenario('invalid_payload',wrong_payload=True)
scenario('entry_failure_restores',native=True,entry_error=True)
scenario('initially_inactive',active=False)
scenario('option_off',enabled=False)
scenario('restore_denied',restore_error=True)
scenario('stop_denied',stop_error=True)
scenario('fd_cleanup_error_still_restores',close_error=True)
(root/'remote-report.json').write_text(json.dumps(dict(success=True,physical_hardware_opened=False,real_linux_ioctl_tested=False,cases=reports),indent=2),encoding='utf-8')
print('PASS: Linux USB bridge control/identity/error handling; no real sudo/USB access')
