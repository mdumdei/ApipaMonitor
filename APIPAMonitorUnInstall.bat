@c:
@if exist C:\Windows\Microsoft.NET\Framework64\v4.0.30319\installutil.exe goto NEXT
@echo Missing required .NET Framework libraries, unable to uninstall...
@goto END
@:NEXT
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\installutil.exe /u "c:\bin\APIPA Monitor.exe"
@:END