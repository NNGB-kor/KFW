"""Service restoration paths only. Firmware/USB/sudo are simulated; no device is opened."""
import contextlib,hashlib,io,json,os,pathlib,subprocess,sys,tempfile
from unittest.mock import patch

source=pathlib.Path(sys.argv[1]).read_text(encoding='utf-8-sig')
ns={'__name__':'test_restore'};exec(compile(source,'rp2040_install.py','exec'),ns)
reports={}
for name in ('success','transfer_failure','inactive','option_off','stop_denied','start_denied','start_inactive'):
    calls=[];out=io.StringIO();target=dict(kind='bootsel',usb_path='/sys/devices/fixture',device='/sys/devices/fixture',vid='2e8a',pid='0003',serial='fixture')
    def sudo(args,password):
        assert password=='fake-password';calls.append(args)
        if name=='stop_denied' and args[1]=='stop' or name=='start_denied' and args[1]=='start':raise RuntimeError('mock denial')
    def run(args,**kwargs):return subprocess.CompletedProcess(args,3 if name=='inactive' or name=='start_inactive' and ['systemctl','start','klipper'] in calls else 0)
    class BadWrite:
        def __enter__(self):return self
        def __exit__(self,*args):pass
        def write(self,*args):raise OSError('simulated disconnect')
    with tempfile.TemporaryDirectory() as directory:
        previous=os.getcwd();os.chdir(directory)
        try:
            image=b'fixture';pathlib.Path('katapult.uf2').write_bytes(image);volume=pathlib.Path('volume').resolve();volume.mkdir()
            ns['cfg']=dict(selected=target,mcu='rp2040',replace_bootloader=True,sha256=hashlib.sha256(image).hexdigest(),stop=name!='option_off',service='klipper',communication='UART',application_start=0x10000000)
            def ready(target,board,password,prepared):prepared.update(device='/dev/fixture',mountpoint=str(volume),epoch=(1,2),signature=(3,4))
            replacements=dict(usb_info=lambda p:dict(target),scan_bootsel=lambda:[dict(target)],wait_storage=ready,usb_epoch=lambda t:(1,2),block_for=lambda p:'/dev/fixture',block_signature=lambda d:(3,4),existing_mount=lambda d:str(volume),sudo=sudo)
            with contextlib.ExitStack() as stack:
                stack.enter_context(patch.dict(ns,replacements));stack.enter_context(patch.object(subprocess,'run',run));stack.enter_context(contextlib.redirect_stdout(out))
                if name=='transfer_failure':stack.enter_context(patch.dict(ns,{'open':lambda *a,**kw:BadWrite()}))
                result=ns['install']('fake-password')
            stopped=name not in ('inactive','option_off','stop_denied')
            assert (['systemctl','start','klipper'] in calls)==stopped
            if name in ('start_denied','start_inactive'):assert result['service_restore_failed'] and result['exit_code']!=0
            elif stopped:assert result['service_restarted']
            if name=='transfer_failure':assert not result['transferred'] and result['exit_code']!=0
            assert 'fake-password' not in out.getvalue()
            reports[name]=dict(exit_code=result['exit_code'],service_actions=calls)
        finally:os.chdir(previous)
pathlib.Path(sys.argv[1]).with_name('bootsel-restore-report.json').write_text(json.dumps(dict(success=True,cases=reports),indent=2),encoding='utf-8')
print('PASS: BOOTSEL service restoration (7 simulated cases).')
