$ghDll = 'C:/Program Files/Rhino 8/Plug-ins/Grasshopper/Grasshopper.dll'
$asm = [System.Reflection.Assembly]::ReflectionOnlyLoadFrom($ghDll)

$allTypes = @()
try { $allTypes = $asm.GetTypes() }
catch [System.Reflection.ReflectionTypeLoadException] { $allTypes = $_.Exception.Types | Where-Object { $_ -ne $null } }

# GH_ComponentServer
$cs = $allTypes | Where-Object { $_.Name -eq "GH_ComponentServer" } | Select-Object -First 1
if ($cs) {
    Write-Host "=== GH_ComponentServer methods:"
    $flags = [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Instance
    $cs.GetMethods($flags) | Where-Object { -not $_.Name.StartsWith("get_") -and -not $_.Name.StartsWith("set_") } | ForEach-Object {
        $params = $_.GetParameters() | ForEach-Object { "$($_.ParameterType.Name) $($_.Name)" }
        Write-Host "  $($_.ReturnType.Name) $($_.Name)($($params -join ', '))"
    }
}

# IGH_ObjectProxy
$proxy = $allTypes | Where-Object { $_.Name -eq "IGH_ObjectProxy" } | Select-Object -First 1
if ($proxy) {
    Write-Host "`n=== IGH_ObjectProxy members:"
    $proxy.GetMembers() | ForEach-Object { Write-Host "  $($_.MemberType): $($_.Name)" }
}

# GH_Document
$doc = $allTypes | Where-Object { $_.Name -eq "GH_Document" } | Select-Object -First 1
if ($doc) {
    Write-Host "`n=== GH_Document AddObject signature:"
    $doc.GetMethods([System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Instance) | Where-Object { $_.Name -eq "AddObject" } | ForEach-Object {
        $params = $_.GetParameters() | ForEach-Object { "$($_.ParameterType.Name) $($_.Name)" }
        Write-Host "  AddObject($($params -join ', '))"
    }
}
