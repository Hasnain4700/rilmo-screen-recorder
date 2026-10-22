# Windows PowerShell 5.1 test of receipt/file helpers only. No registry or device mutation.
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$script=Join-Path $PSScriptRoot 'setup-virtual-camera.ps1'
$tokens=$null;$parseErrors=$null
$ast=[System.Management.Automation.Language.Parser]::ParseFile($script,[ref]$tokens,[ref]$parseErrors)
if($parseErrors.Count){throw 'Setup script has parse errors.'}
foreach($name in @('Assert-NoReparse','Read-ValidatedReceiptFiles','Save-Receipt','Remove-OwnedCameraFiles')) {
    $fn=$ast.FindAll({param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst]},$true)|Where-Object Name -eq $name
    if(@($fn).Count -ne 1){throw ('Missing helper '+$name)}
    . ([scriptblock]::Create($fn.Extent.Text))
}
$installRoot=Join-Path $root ('artifacts\camera-setup-smoke\'+[Guid]::NewGuid().ToString('N'))
$receiptPath=Join-Path $installRoot 'installation.json'
$classId='{9E7FF7BC-AA7A-4B1D-8A9E-DDDFB0692DFD}'
$deviceName='Rilmo Virtual Camera';$provenancePath='test-only'
$version=Join-Path $installRoot 'test-version';New-Item -ItemType Directory -Path $version -Force|Out-Null
$file=Join-Path $version 'rilmo-virtual-camera64.dll';Set-Content -LiteralPath $file -Value 'isolated fixture'
Save-Receipt @($file)
if(@(Read-ValidatedReceiptFiles).Count -ne 1){throw 'Valid receipt rejected.'}
Save-Receipt @((Join-Path $root 'outside.dll'))
$rejected=$false;try{Read-ValidatedReceiptFiles|Out-Null}catch{$rejected=$true};if(!$rejected){throw 'Escaping receipt accepted.'}
Set-Content -LiteralPath $receiptPath -Value '{malformed'
$rejected=$false;try{Read-ValidatedReceiptFiles|Out-Null}catch{$rejected=$true};if(!$rejected){throw 'Malformed receipt accepted.'}
Save-Receipt @($file)
$handle=[IO.File]::Open($file,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::None)
try{
    $left=@(Remove-OwnedCameraFiles @($file))
    if($left.Count -ne 1 -or !(Test-Path -LiteralPath $file)){throw 'Locked file was not retained.'}
    Save-Receipt $left
    if(@(Read-ValidatedReceiptFiles).Count -ne 1){throw 'Locked-file cleanup receipt was lost.'}
}finally{$handle.Dispose()}
if(@(Remove-OwnedCameraFiles @($file)).Count -ne 0){throw 'Unlocked cleanup failed.'}
if(Test-Path -LiteralPath $version){throw 'Empty owned version folder was not removed.'}
Remove-Item -LiteralPath $receiptPath
Remove-Item -LiteralPath $installRoot
Write-Output 'PASS: valid/malformed/escaping receipts, locked-file retention and retry, exact empty-directory cleanup. No device registration performed.'
