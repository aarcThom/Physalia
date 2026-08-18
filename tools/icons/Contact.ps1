# Builds a magnified contact sheet of the extracted 24x24 icons so the alpha keying and the
# downscale can be judged against a Grasshopper-ish canvas grey rather than against black.
param(
  [string]$Dir = 'C:\Users\rober\AppData\Local\Temp\claude\C--Users-rober-repos-Physalia\2caff6f7-7054-4faf-b9b4-adbecfcb6b96\scratchpad\icons\out',
  [string]$Out,
  [int]$Zoom = 4,
  [int]$Cols = 10,
  [int]$Bg = 212
)
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
$files = Get-ChildItem $Dir -Filter *.png | Sort-Object Name
$cell = 24 * $Zoom + 8
$rows = [Math]::Ceiling($files.Count / $Cols)
$bmp = New-Object System.Drawing.Bitmap ($Cols * $cell), ($rows * $cell)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::FromArgb($Bg, $Bg, $Bg))
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
for ($i = 0; $i -lt $files.Count; $i++) {
  $ic = [System.Drawing.Bitmap]::FromFile($files[$i].FullName)
  $x = ($i % $Cols) * $cell + 4
  $y = [Math]::Floor($i / $Cols) * $cell + 4
  $g.DrawImage($ic, (New-Object System.Drawing.Rectangle $x, $y, (24*$Zoom), (24*$Zoom)))
  $ic.Dispose()
}
$g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output "$($files.Count) icons -> $Out"
$files.Name -join ', '
