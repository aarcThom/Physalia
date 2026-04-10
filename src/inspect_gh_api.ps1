Add-Type -AssemblyName "System.Windows.Forms"
Add-Type -AssemblyName "System.Drawing"

$rhinoDir = 'C:/Program Files/Rhino 8/System'
$ghDir    = 'C:/Program Files/Rhino 8/Plug-ins/Grasshopper'

foreach ($dll in @('RhinoCommon.dll','Eto.dll','Eto.WinForms.dll')) {
    $p = Join-Path $rhinoDir $dll
    if (Test-Path $p) { try { [System.Reflection.Assembly]::LoadFrom($p) | Out-Null } catch {} }
}
$ghAsm = [System.Reflection.Assembly]::LoadFrom("$ghDir/Grasshopper.dll")
$allTypes = @()
try { $allTypes = $ghAsm.GetTypes() }
catch [System.Reflection.ReflectionTypeLoadException] { $allTypes = $_.Exception.Types | Where-Object { $_ -ne $null } }

$pubFlags = [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Instance
$allMemberFlags = [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::NonPublic -bor [System.Reflection.BindingFlags]::Instance

# GH_Canvas overlay event delegate signature
Write-Host "=== CanvasPostPaintOverlay delegate Invoke signature:"
$delegateType = $allTypes | Where-Object { $_.FullName -eq 'Grasshopper.GUI.Canvas.GH_Canvas+CanvasPostPaintOverlayEventHandler' } | Select-Object -First 1
if ($delegateType) {
    $invoke = $delegateType.GetMethod('Invoke')
    $params = $invoke.GetParameters() | ForEach-Object { "$($_.ParameterType.FullName) $($_.Name)" }
    Write-Host "  Invoke($($params -join ', '))"
}

# What is GH_PaintEventArgs?
Write-Host "`n=== Types matching 'PaintEvent':"
$allTypes | Where-Object { $_.Name -match 'PaintEvent' } | ForEach-Object {
    $t = $_
    Write-Host "  $($t.FullName)"
    $t.GetProperties($pubFlags) | ForEach-Object { try { Write-Host "    prop: $($_.PropertyType.Name) $($_.Name)" } catch {} }
}

# GH_Canvas Graphics property + any paint-related properties
$canvas = $allTypes | Where-Object { $_.FullName -eq 'Grasshopper.GUI.Canvas.GH_Canvas' } | Select-Object -First 1
Write-Host "`n=== GH_Canvas all public properties:"
$canvas.GetProperties($pubFlags) | ForEach-Object { try { Write-Host "  $($_.PropertyType.Name) $($_.Name)" } catch {} }

# GH_CanvasWidget_FixedObject — Ratio setter?
$fwo = $allTypes | Where-Object { $_.FullName -eq 'Grasshopper.GUI.Widgets.GH_CanvasWidget_FixedObject' } | Select-Object -First 1
Write-Host "`n=== GH_CanvasWidget_FixedObject constructors:"
$fwo.GetConstructors() | ForEach-Object {
    $params = $_.GetParameters() | ForEach-Object { "$($_.ParameterType.Name) $($_.Name)" }
    Write-Host "  ctor($($params -join ', '))"
}
Write-Host "--- All fields (incl. protected):"
$fwo.GetFields([System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::NonPublic -bor [System.Reflection.BindingFlags]::Instance) | ForEach-Object {
    Write-Host "  $($_.FieldType.Name) $($_.Name)"
}
