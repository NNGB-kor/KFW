using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
namespace KlipperWinForms {
    using Obj=Dictionary<string,object>;
    static class Rp2040Image {
        internal static Obj Inspect(string path) {
            if(!Path.GetFileName(path).Equals("katapult.withclear.uf2",StringComparison.OrdinalIgnoreCase))
                throw new Exception("이 설치 기능에는 Katapult 빌드 폴더의 katapult.withclear.uf2를 선택하세요.");
            var directory=Path.GetDirectoryName(Path.GetFullPath(path));
            if(File.Exists(Path.Combine(directory,"BUILD_INCOMPLETE"))) throw new Exception("완료되지 않은 빌드입니다.");
            var report=FirmwareExport.Report(path);
            string mcu=Json.S(report,"mcu");
            if(Json.S(report,"status")!="success" || Json.S(report,"project")!="katapult" || !new[]{"rp2040","rp2350"}.Contains(mcu))
                throw new Exception("RP2040/RP2350 Katapult 빌드 기록이 필요합니다.");
            if(new FileInfo(path).Length>1024*1024) throw new Exception("Katapult UF2 크기가 너무 큽니다.");
            byte[] bytes=File.ReadAllBytes(path);
            var artifact=Json.D(Json.D(report,"artifacts"),"katapult.withclear.uf2");
            string hash=U.Hash(bytes);
            if(hash!=Json.S(artifact,"sha256") || bytes.Length!=Convert.ToInt64(artifact["size"])) throw new Exception("UF2가 빌드 기록과 일치하지 않습니다.");
            uint app=checked((uint)U.Number(Json.S(report,"application_address")));
            ValidateFor(bytes,app,mcu);
            return new Obj{{"mcu",mcu},{"bootsel_pid",mcu=="rp2040"?3:15},{"board_id",mcu=="rp2040"?"RPI-RP2":"RP2350"},{"version",Json.S(report,"source_version")},{"sha256",hash},{"application_start",app},
                {"communication",Json.S(report,"communication")},{"size",bytes.Length},{"path",Path.GetFullPath(path)}};
        }
        internal static void Validate(byte[] bytes,uint app)=>ValidateFor(bytes,app,"rp2040");
        internal static void ValidateFor(byte[] bytes,uint app,string mcu) {
            if(mcu!="rp2040"&&mcu!="rp2350")throw new Exception("Unsupported RP MCU");uint family=mcu=="rp2040"?0xe48bff56u:0xe48bff59u;
            if(app<0x10002000 || app>0x10040000 || app%4096!=0 || bytes.Length<1024 || bytes.Length%512!=0) throw new Exception("잘못된 RP Katapult 주소/크기입니다.");
            int count=bytes.Length/512; var addresses=new HashSet<uint>(); bool start=false,cleared=false;
            for(int i=0;i<count;i++) {
                int p=i*512; uint address=U.V32(bytes,p+12);
                if(U.V32(bytes,p)!=0x0a324655 || U.V32(bytes,p+4)!=0x9e5d5157 || U.V32(bytes,p+508)!=0x0ab16f30 ||
                   U.V32(bytes,p+8)!=0x2000 || U.V32(bytes,p+16)!=256 || U.V32(bytes,p+20)!=(uint)i ||
                   U.V32(bytes,p+24)!=(uint)count || U.V32(bytes,p+28)!=family || address%256!=0 || !addresses.Add(address))
                    throw new Exception("UF2 헤더/블록/MCU 식별값 오류입니다.");
                if(address==app) { if(bytes.Skip(p+32).Take(256).Any(b=>b!=0)) throw new Exception("Klipper 시작 영역 초기화 블록이 아닙니다."); cleared=true; }
                else if(address<0x10000000 || (ulong)address+256>app) throw new Exception("UF2가 Katapult 설치 영역을 벗어납니다.");
                if(address==0x10000000) start=true;
            }
            if(!start || !cleared) throw new Exception("부트로더 시작 블록 또는 애플리케이션 초기화 블록이 없습니다.");
        }
        internal static byte[] Snapshot(Obj request,out Obj info) {
            info=Inspect(Json.S(request,"firmware"));
            var bytes=File.ReadAllBytes(Json.S(info,"path"));
            if(Json.S(request,"sha256")!=Json.S(info,"sha256") || U.Hash(bytes)!=Json.S(info,"sha256")) throw new Exception("선택 후 UF2가 변경되었습니다. 다시 선택하세요.");
            return bytes;
        }
    }
}
