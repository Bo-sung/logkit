' LogServer 숨김 실행 런처. 작업 디렉터리를 서버 폴더로 고정한 뒤 숨김 모드로 실행한다.
Set sh = CreateObject("WScript.Shell")
sh.CurrentDirectory = "C:\UnityCI\LogServer"
sh.Run """C:\UnityCI\LogServer\LogServer.exe""", 0, False
