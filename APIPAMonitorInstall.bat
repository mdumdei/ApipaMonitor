@c:
@if exist C:\Windows\Microsoft.NET\Framework64\v4.0.30319\installutil.exe goto NEXT
@echo Missing required .NET Framework libraries, unable to install...
@goto END
@:NEXT
@if not exist c:\bin\*.* md c:\bin
@cd bin
@copy "\\bulldog.net\internal\Utilities\APIPA Monitor\bin\Release\APIPA Monitor.exe" c:\bin
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\installutil.exe "c:\bin\APIPA Monitor.exe"
net start apipamon
@:END