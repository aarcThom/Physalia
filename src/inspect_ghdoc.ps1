$rhinoDir = 'C:/Program Files/Rhino 8/System'
foreach ($dll in @('RhinoCommon.dll','Eto.dll','Eto.WinForms.dll')) {
    $p = Join-Path $rhinoDir $dll
    if (Test-Path $p) { try { [System.Reflection.Assembly]::LoadFrom($p) | Out-Null } catch {} }
}
[System.Reflection.Assembly]::LoadFrom('C:/Program Files/Rhino 8/Plug-ins/Grasshopper/Grasshopper.dll') | Out-Null
$ghIoAsm = [System.Reflection.Assembly]::LoadFrom('C:/Program Files/Rhino 8/Plug-ins/Grasshopper/GH_IO.dll')

$allTypes = @()
try { $allTypes = $ghIoAsm.GetTypes() }
catch [System.Reflection.ReflectionTypeLoadException] { $allTypes = $_.Exception.Types | Where-Object { $_ -ne $null } }

$archive = $allTypes | Where-Object { $_.FullName -eq 'GH_IO.Serialization.GH_Archive' } | Select-Object -First 1
Write-Host "=== GH_Archive public methods:"
$archive.GetMethods([System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Instance) | ForEach-Object {
    $params = $_.GetParameters() | ForEach-Object { "$($_.ParameterType.Name) $($_.Name)" }
    Write-Host "  $($_.ReturnType.Name) $($_.Name)($($params -join ', '))"
}
