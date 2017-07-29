$SrcFilePath = "\\bulldog.net\internal\Utilities\APIPA Monitor\APIPA Monitor.exe"
$LocalFilePath = "C:\bin\APIPA Monitor.exe"

function Get-FileVersion([ValidateScript({
        If(Test-Path $_){$true}else{Throw "Invalid path given: $_"}
        })][string]$Path){
	$VersionInfo = (Get-Item $Path).VersionInfo
	return ("{0}.{1}.{2}.{3}" -f $VersionInfo.FileMajorPart, 
    		$VersionInfo.FileMinorPart, 
    		$VersionInfo.FileBuildPart, 
	    $VersionInfo.FilePrivatePart)
}

function Validate-FileVersion([ValidateScript({
        If(Test-Path $_){$true}else{Throw "Invalid source path given: $_"}
        })][string]$Reference,[ValidateScript({
        If(Test-Path $_){$true}else{Throw "Invalid target path given: $_"}
        })][string]$Test){

        return $(Get-FileVersion $Test) -ge $(Get-FileVersion $Reference)
        }



try {
    return $(Validate-FileVersion -Test $LocalFilePath -Reference $SrcFilePath)
    } catch {
        return $false
        }