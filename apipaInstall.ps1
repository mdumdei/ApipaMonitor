[string]$ResetNICHandler_ps1 = @'
# APIPA Monitor Email Alert - Part 1 of 2
#  This is part of 1 of a 2 part handler for APIPA'd or non-responding NICS. Part 1 generates an
#  HTML message body and stores it locally. Part 2 scans the local folder every x minutes and if
#  the network is back up, it sends the HTML message to the notification list. 
# See apipamonMailQueueHandler.ps1 for the part 2 component.

[System.Diagnostics.EventLogEntry[]]$events = get-eventlog -LogName Application -source "apipamon" -newest 10
[string]$head= @"
<head> 
<style> 
  body {background-color:white; }
  table {border-width: 1px;border-style: solid;border-color: black;border-collapse: collapse; }
  th {border-width: 1px;padding: 2px;border-style: solid;border-color: black;background-color:#63cff5 }
  td {border-width: 1px;padding: 3px;border-style: solid;border-color: black;background-color:white}
</style>
</head>
"@
[string]$tbl="<table><tr><th>Date/Time</th><th>Level</th><th>EventID</th><th>Message</th></tr>`r`n" 

for ([int]$i = 0; $i -lt $events.Count; ++$i) {
    $when = $events[$i].TimeGenerated
    $level = $events[$i].EntryType
    $id = $events[$i].EventID
    $msg = $events[$i].Message
    $tbl += "<tr><td>$when</td><td>$level</td><td>$id</td><td>$msg</td></tr>`r`n"
}
$tbl += "</table>"
$guid = [guid]::NewGuid()
"<html>$head<body>$tbl</body></html>" | Out-File "C:\bin\apipamon\APIPAmsg_$guid.html"
'@

[string] $MailQueueHandler_ps1 = @'
# APIPA Monitor Email Alert - Part 2 of 2
#  This is part of 2 of a 2 part handler for APIPA'd or non-responding NICS. Part 1 generates an
#  HTML message body and stores it locally. Part 2 scans the local folder every x minutes and if
#  the network is back up, it sends the HTML message to the noti(fication list. '=
# See apipamonResetNICHandler.ps1 for the part 1 component.

$script:server = "mail.texarkanacollege.edu"

Function Test-SMTP
{
    for ($i = 0; $i -lt 3; ++$i) {
        if ($i -gt 0) {
            Start-Sleep -s 5
        }
        if ((Test-Connection -ComputerName $script:server -BufferSize 16 -Count 1 -ErrorAction 0 -Quiet) -eq $true) {
            return $true
        }
    }
    return $false
}

if (Test-SMTP -eq $true) 
{
    [string[]] $recipients =  "_TC Infrastructure Services Team <infrasvcteam@texarkanacollege.edu>"
    [string]$host = $env:COMPUTERNAME
    [string]$subject = "APIPA Monitor reset the NIC on " + $host
    [string]$from = "apipamon@$($host.ToLower()).bulldog.net"
    $msgFiles = $(Get-ChildItem -Path "c:\bin\apipamon" -Filter "APIPAmsg_*" | Sort -Property LastWriteTime)
    if ($msgFiles.Count -gt 5) {
        $newMsgFiles = @($msgFiles[0], $msgFiles[1], $msgFiles[$msgFiles.Count - 2], $msgFiles[$msgFiles.Count - 1])         
        $msgFiles = $newMsgFiles     
    }
    foreach ($f in $msgFiles)
    {
        [string]$body = Get-Content "c:\bin\apipamon\$f"
        $recipients | Out-File "c:\bin\apipamon\lastMsg.txt"
        $from | Out-File -Append "c:\bin\apipamon\lastMsg.txt"
        $script:server | Out-File -Append "c:\bin\apipamon\lastMsg.txt"
        $body | Out-File -Append "c:\bin\apipamon\lastMsg.txt"
        Send-MailMessage -To $recipients -From $from -Subject $subject -SmtpServer $script:server -Body $body -BodyAsHtml
    }
    Remove-Item "c:\bin\apipamon\APIPAmsg_*"
}
'@


[string]$ResetNICScheduledTask_xml = @'
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo>
    <Date>2017-09-04T22:15:25.8690684</Date>
    <Author>BULLDOG\michael.dumdei</Author>
    <Description>Creates email body when registered APIPA Monitor event occurs.</Description>
    <URI>\APIPA Monitor Error Alert</URI>
  </RegistrationInfo>
  <Triggers>
    <EventTrigger>
      <Enabled>true</Enabled>
      <Subscription>&lt;QueryList&gt;&lt;Query Id="0" Path="Application"&gt;&lt;Select Path="Application"&gt;*[System[Provider[@Name='apipamon'] and EventID=9997]]&lt;/Select&gt;&lt;/Query&gt;&lt;/QueryList&gt;</Subscription>
    </EventTrigger>
    <EventTrigger>
      <Enabled>true</Enabled>
      <Subscription>&lt;QueryList&gt;&lt;Query Id="0" Path="Application"&gt;&lt;Select Path="Application"&gt;*[System[Provider[@Name='apipamon'] and EventID=9998]]&lt;/Select&gt;&lt;/Query&gt;&lt;/QueryList&gt;</Subscription>
    </EventTrigger>
    <EventTrigger>
      <Enabled>true</Enabled>
      <Subscription>&lt;QueryList&gt;&lt;Query Id="0" Path="Application"&gt;&lt;Select Path="Application"&gt;*[System[Provider[@Name='apipamon'] and EventID=9999]]&lt;/Select&gt;&lt;/Query&gt;&lt;/QueryList&gt;</Subscription>
    </EventTrigger>
  </Triggers>
  <Principals>
    <Principal id="Author">
      <UserId>S-1-5-18</UserId>
      <RunLevel>LeastPrivilege</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>true</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>false</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>true</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT1H</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context="Author">
    <Exec>
      <Command>powershell.exe</Command>
      <Arguments>-NoProfile -ExecutionPolicy ByPass "c:\bin\apipamon\ResetNICHandler.ps1"</Arguments>
    </Exec>
  </Actions>
</Task>
'@


[string]$MailQueueScheduledTask_xml = @'
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo>
    <Date>2017-09-04T22:15:25.8690684</Date>
    <Author>BULLDOG\michael.dumdei</Author>
    <Description>Sends APIPA Monitor error emails SMTP server is available.</Description>
    <URI>\APIPA Monitor Reset NIC Handler Part 2</URI>
  </RegistrationInfo>
  <Triggers>
    <TimeTrigger>
      <Repetition>
        <Interval>PT5M</Interval>
        <StopAtDurationEnd>false</StopAtDurationEnd>
      </Repetition>
      <StartBoundary>2017-09-06T15:07:26</StartBoundary>
      <ExecutionTimeLimit>PT30M</ExecutionTimeLimit>
      <Enabled>true</Enabled>
    </TimeTrigger>
    <EventTrigger>
      <Enabled>true</Enabled>
      <Subscription>&lt;QueryList&gt;&lt;Query Id="0" Path="Application"&gt;&lt;Select Path="Application"&gt;*[System[Provider[@Name='apipamon'] and EventID=1002]]&lt;/Select&gt;&lt;/Query&gt;&lt;/QueryList&gt;</Subscription>
    </EventTrigger>
  </Triggers>
  <Principals>
    <Principal id="Author">
      <UserId>S-1-5-18</UserId>
      <RunLevel>LeastPrivilege</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>true</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>false</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>true</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT1H</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context="Author">
    <Exec>
      <Command>powershell.exe</Command>
      <Arguments>-NoProfile -ExecutionPolicy ByPass "c:\bin\apipamon\MailQueueHandler.ps1"</Arguments>
    </Exec>
  </Actions>
</Task>
'@

if ((Test-Path "c:\bin")  -eq $false) {
    New-Item -ItemType Directory -Path "c:\bin"
}
if ((Test-Path "c:\bin\apipamon") -eq $false) {
    New-Item -ItemType Directory -Path "c:\bin\apipamon"
    & "C:\Windows\System32\icacls.exe" c:\bin\apipamon /grant "bulldog\_TC_TaskEmailAccount:(OI)(CI)(F)"
}
[string[]]$paths = "c:\bin\", "c:\bin\apipamon\"
foreach ($path in $paths) {
    if ((Test-Path "$path\APIPA Monitor.exe") -eq $true) {
        $svc = Get-Service -Name "apipamon*"
        if ($svc -ne $null) {
            if ($svc.Status -eq "Running") {
                $svc.Stop();
                $svc.WaitForStatus("Stopped", "00:00:15")
                $svc.Refresh()
                if ($svc.Status -ne "Stopped") {
                    throw "Unable to stop apipamon service"
                }
            }
            $svc = Get-WmiObject -Class Win32_Service -Filter "Name='apipamon'"
            $svc.delete() | Out-Null
        }
    }
    Remove-Item "$path\APIPA*.*"
}

$ResetNICHandler_ps1 | Set-Content "c:\bin\apipamon\ResetNICHandler.ps1"
$MailQueueHandler_ps1 | Set-Content "c:\bin\apipamon\MailQueueHandler.ps1"

Register-ScheduledTask -Force -TaskName "APIPA Monitor Reset NIC Handler" -Xml $ResetNICScheduledTask_xml  -User "NT Authority\SYSTEM" 
Register-ScheduledTask -Force -TaskName "APIPA Monitor Mail Queue Handler" -Xml $MailQueueScheduledTask_xml -User "NT Authority\SYSTEM" 
& "C:\Windows\System32\icacls.exe" "c:\bin\apipamon\*ps1" /inheritance:r
& "C:\Windows\System32\icacls.exe" "c:\bin\apipamon\*ps1" /grant "NT Authority\SYSTEM:F"
& "C:\Windows\System32\icacls.exe" "c:\bin\apipamon\*ps1" /grant "BUILTIN\Administrators:F"
& "C:\Windows\System32\icacls.exe" "c:\bin\apipamon\*ps1" /grant "BULLDOG\_TC_TaskEmailAccount:RX"

Copy-Item "\\bulldog.net\internal\Utilities\APIPAMonitor\APIPA Monitor.exe" -Destination "c:\bin\apipamon"
& "c:\bin\apipamon\APIPA Monitor.exe" -install -m txk-mb-01.bulldog.net


