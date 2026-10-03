using System;using System.Collections.Generic;
namespace KlipperWinForms {using Obj=Dictionary<string,object>;
 sealed class GitHubQuotaException:Exception {
  internal GitHubQuotaException(string reset,Exception cause):base(MessageFor(reset),cause){}
  static string MessageFor(string reset){long seconds;string time=long.TryParse(reset,out seconds)?" 다시 확인 가능한 시각: "+DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime().ToString("MM.dd HH:mm:ss")+".":" 잠시 후 다시 확인하세요.";return "GitHub API 조회 한도에 도달하여 메타데이터를 확인하지 못했습니다."+time+" 소스 버전이 삭제됐다는 의미는 아닙니다.";}
 }
 sealed class VersionStageException:Exception {
  internal readonly string Stage;
  internal VersionStageException(string stage,string message,Exception cause):base(message+"\r\n"+cause.Message,cause){Stage=stage;}
 }
 static class VersionStatus {
  internal static bool IsQuota(Exception error){for(var e=error;e!=null;e=e.InnerException)if(e is GitHubQuotaException)return true;return false;}
  internal static string Failure(Exception error){if(IsQuota(error))return "파일 목록 조회 대기 · GitHub 요청 제한.";if(error is VersionStageException&&((VersionStageException)error).Stage=="configuration")return "다운로드 완료 · 설정 읽기 오류.";return "소스 받기 미완료. 다시 시도하세요.";}
  internal static string Notice(Obj state){switch(Json.S(state,"notice_stage")){case "catalog":return "다운로드 완료 · 최신 여부 미확인.";case "download":return "저장된 소스 사용 · 새 소스 받기 미완료.";case "build":return "다운로드 완료 · 빌드 준비 확인 필요.";default:return "다운로드 완료.";}}
 }
}
