param([string]$VisualStudioPath, [string]$PackagePath = (Join-Path $PSScriptRoot 'RinkuPowerTools.SqlEditor.vsix'))

$ErrorActionPreference = 'Stop'
if (!$VisualStudioPath) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $VisualStudioPath = & $vswhere -latest -products '*' -property installationPath
}
if (!(Test-Path -LiteralPath $PackagePath -PathType Leaf)) {
    throw "Native SQL editor package not found: $PackagePath. Build PowerTools with Visual Studio MSBuild first."
}
$installer = Join-Path $VisualStudioPath 'Common7\IDE\VSIXInstaller.exe'
& $installer $PackagePath
if ($LASTEXITCODE -ne 0) {
    throw "Visual Studio extension installation failed with exit code $LASTEXITCODE."
}
$syntaxPath = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.vs\Extensions\RinkuSql\Syntaxes'
foreach ($name in @('RinkuSql.tmLanguage.json', 'Sql.plist')) {
    $path = Join-Path $syntaxPath $name
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        Remove-Item -LiteralPath $path
    }
}
Write-Host 'Restart Visual Studio and reopen .rnksql documents in the SQL editor.'
