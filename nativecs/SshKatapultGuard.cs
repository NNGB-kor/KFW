using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace KlipperWinForms {
    static class SshKatapultGuard {
        internal static string Apply(string upstream, Dictionary<string,object> info, Dictionary<string,object> selected, bool check) {
            foreach(string api in new[]{"class CanFlasher", "async def connect_btl(self)", "async def send_command(", "async def send_file(self)", "async def verify_file(self)", "async def finish(self)", "def _open_device(self, device", "def get_usb_path("})
                if(!upstream.Contains(api)) throw new Exception("설치된 Katapult 스크립트 API를 지원하지 않습니다: " + api);
            if(Json.S(selected,"usb_path")=="") throw new Exception("원격 USB 물리 위치를 확인할 수 없습니다.");
            var config = new Dictionary<string,object> {
                {"mcu",Json.S(info,"mcu")}, {"start",Convert.ToInt64(info["application_start"])},
                {"hash",Json.S(info,"sha256")}, {"location",Json.S(selected,"usb_path")},
                {"check",check}, {"uart",SshServiceRunner.NeedsSerialRequest(selected)}
            };
            string guard = "\n# C# client guards for this private operation.\n_cs_cfg = json.loads(" + U.CString(Json.Write(config)) + ")\n" + @"
_cs_command = CanFlasher.send_command
_cs_connect = CanFlasher.connect_btl
_cs_send = CanFlasher.send_file
_cs_verify = CanFlasher.verify_file
_cs_finish = CanFlasher.finish
_cs_open = SerialSocket._open_device

async def _cs_guard_command(self, name, *args, **kwargs):
    if name != 'CONNECT':
        return await _cs_command(self, name, *args, **kwargs)
    if _cs_cfg['uart']:
        output_line('CH340/CH341: checking for Katapult on the open UART connection.')
        try:
            response = await _cs_command(self, name, tries=2, read_timeout=0.5)
            output_line('Katapult already responding; UART entry request skipped.')
        except FlashError:
            output_line('No Katapult response; requesting UART bootloader on the same connection.')
            self.node.write(SERIAL_BL_REQ)
            await asyncio.sleep(1.0)
            response = await _cs_command(self, name, tries=5, read_timeout=2.0)
    else:
        response = await _cs_command(self, name, *args, **kwargs)
    if len(response) < 13:
        raise FlashError('Invalid CONNECT identity payload')
    self._cs_mcu = response[12:].split(b'\0', 1)[0].decode().strip().lower()
    self._cs_start = struct.unpack('<I', response[4:8])[0]
    return response

def _cs_compatible(self):
    if getattr(self, '_cs_mcu', None) != _cs_cfg['mcu'] or getattr(self, '_cs_start', None) != _cs_cfg['start']:
        raise FlashError('MCU/application start mismatch; operation blocked')

async def _cs_return_application(self):
    await _cs_finish(self)
    # COMPLETE ACK confirms acceptance, not successful execution of the application.
    await asyncio.sleep(1.0)
    pathlib.Path('application-ready').write_text('COMPLETE acknowledged\n')
    output_line('Klipper application start command acknowledged.')

async def _cs_guard_connect(self):
    await _cs_connect(self)
    _cs_compatible(self)
    self._cs_connected = True
    if _cs_cfg['check']:
        # Status-only: no firmware writes; restore the existing application after validation.
        await _cs_return_application(self)

async def _cs_guard_send(self):
    if _cs_cfg['check'] or not getattr(self, '_cs_connected', False):
        raise FlashError('Firmware write blocked in status mode or before validated CONNECT')
    _cs_compatible(self)
    if hashlib.sha256(self.firmware_path.read_bytes()).hexdigest() != _cs_cfg['hash']:
        raise FlashError('Firmware snapshot hash mismatch')
    await _cs_send(self)

async def _cs_guard_verify(self):
    await _cs_verify(self)
    self._cs_verified = True

async def _cs_guard_finish(self):
    if getattr(self, '_cs_verified', False):
        await _cs_return_application(self)

def _cs_guard_open(self, device, baud):
    physical = get_usb_path(pathlib.Path(device))
    if physical is None or str(physical.resolve()) != _cs_cfg['location']:
        raise FlashError('USB physical location mismatch')
    return _cs_open(self, device, baud)

CanFlasher.send_command = _cs_guard_command
CanFlasher.connect_btl = _cs_guard_connect
CanFlasher.send_file = _cs_guard_send
CanFlasher.verify_file = _cs_guard_verify
CanFlasher.finish = _cs_guard_finish
SerialSocket._open_device = _cs_guard_open

";
            var entry = Regex.Match(upstream,@"(?m)^if __name__\s*==\s*['""]__main__['""]\s*:");
            if(!entry.Success) throw new Exception("Katapult CLI entry point not found");
            return upstream.Insert(entry.Index,guard);
        }
    }
}
