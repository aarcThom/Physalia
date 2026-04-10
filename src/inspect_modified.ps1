$rhinoDir = 'C:/Program Files/Rhino 8/System'
foreach ($dll in @('RhinoCommon.dll','Eto.dll','Eto.WinForms.dll')) {
    $p = Join-Path $rhinoDir $dll
    if (Test-Path $p) { try { [System.Reflection.Assembly]::LoadFrom($p) | Out-Null } catch {} }
}
$ghAsm = [System.Reflection.Assembly]::LoadFrom('C:/Program Files/Rhino 8/Plug-ins/Grasshopper/Grasshopper.dll')
$allTypes = @()
try { $allTypes = $ghAsm.GetTypes() }
catch [System.Reflection.ReflectionTypeLoadException] { $allTypes = $_.Exception.Types | Where-Object { $_ -ne $null } }

$doc = $allTypes | Where-Object { $_.FullName -eq 'Grasshopper.Kernel.GH_Document' } | Select-Object -First 1
$flags = [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::NonPublic -bor [System.Reflection.BindingFlags]::Instance

Write-Host "=== Members matching modif/saved/dirty/clean:"
$doc.GetMembers($flags) | Where-Object { $_.Name -match '(?i)modif|saved|dirty|clean|changed' } | ForEach-Object {
    Write-Host "  $($_.MemberType): $($_.Name)"
}

Write-Host "`n=== Fields matching modif/saved/dirty/clean:"
$doc.GetFields($flags) | Where-Object { $_.Name -match '(?i)modif|saved|dirty|clean|changed' } | ForEach-Object {
    Write-Host "  field: $($_.FieldType.Name) $($_.Name)"
}
