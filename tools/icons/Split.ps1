# Splits an icon sprite sheet into named 24x24 transparent PNGs.
#
# Segmentation is projection-based, not grid-based: the generated sheets are not on an even grid,
# and one icon is often several disconnected blobs (a body plus its ground line, an arrow plus its
# box), so per-cell connected-component isolation would tear icons apart. We take the ink profile,
# then merge across the smallest gap until the segment count matches the layout verified by eye --
# self-verifying, since a wrong layout cannot produce a plausible result.
param(
  [Parameter(Mandatory=$true)][string]$Sheet,
  [Parameter(Mandatory=$true)][int[]]$Rows,
  [Parameter(Mandatory=$true)][string[]]$Names,
  [Parameter(Mandatory=$true)][string]$OutDir,
  [int]$Size = 24,
  [int]$Pad = 1,
  [int]$InkThreshold = 40,
  [switch]$ReportOnly
)

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

$bmp = [System.Drawing.Bitmap]::FromFile($Sheet)
$W = $bmp.Width; $H = $bmp.Height
$rect = New-Object System.Drawing.Rectangle 0,0,$W,$H
$data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$stride = $data.Stride
$buf = New-Object byte[] ($stride * $H)
[System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $buf, 0, $buf.Length)
$bmp.UnlockBits($data)

$ink = New-Object 'bool[]' ($W * $H)
for ($y = 0; $y -lt $H; $y++) {
  $ro = $y * $stride; $io = $y * $W
  for ($x = 0; $x -lt $W; $x++) {
    $o = $ro + $x * 4
    $m = [Math]::Max([Math]::Max([int]$buf[$o], [int]$buf[$o+1]), [int]$buf[$o+2])
    if ($m -gt $InkThreshold) { $ink[$io + $x] = $true }
  }
}

function Get-Runs($profile) {
  $runs = New-Object 'System.Collections.Generic.List[int[]]'
  $s = -1
  for ($i = 0; $i -lt $profile.Length; $i++) {
    if ($profile[$i]) { if ($s -lt 0) { $s = $i } }
    elseif ($s -ge 0) { $runs.Add([int[]]@($s, ($i - 1))); $s = -1 }
  }
  if ($s -ge 0) { $runs.Add([int[]]@($s, ($profile.Length - 1))) }
  return ,$runs
}

function Merge-To($runs, [int]$target, [string]$what) {
  $r = New-Object 'System.Collections.Generic.List[int[]]'
  foreach ($x in $runs) { $r.Add([int[]]$x) }
  if ($r.Count -lt $target) { throw "$what : found only $($r.Count) segments, need $target" }
  while ($r.Count -gt $target) {
    $best = -1; $bestGap = [int]::MaxValue
    for ($i = 0; $i -lt $r.Count - 1; $i++) {
      $g = $r[$i+1][0] - $r[$i][1]
      if ($g -lt $bestGap) { $bestGap = $g; $best = $i }
    }
    $r[$best] = [int[]]@($r[$best][0], $r[$best+1][1])
    $r.RemoveAt($best + 1)
  }
  return ,$r
}

$rowProfile = New-Object 'bool[]' $H
for ($y = 0; $y -lt $H; $y++) {
  $io = $y * $W
  for ($x = 0; $x -lt $W; $x++) { if ($ink[$io + $x]) { $rowProfile[$y] = $true; break } }
}
$bands = Merge-To (Get-Runs $rowProfile) $Rows.Count 'rows'

$cells = New-Object 'System.Collections.Generic.List[object]'
for ($b = 0; $b -lt $bands.Count; $b++) {
  $y0 = $bands[$b][0]; $y1 = $bands[$b][1]
  $colProfile = New-Object 'bool[]' $W
  for ($x = 0; $x -lt $W; $x++) {
    for ($y = $y0; $y -le $y1; $y++) { if ($ink[$y * $W + $x]) { $colProfile[$x] = $true; break } }
  }
  $cols = Merge-To (Get-Runs $colProfile) $Rows[$b] "row $($b+1)"
  foreach ($c in $cols) {
    $mnx = $W; $mxx = -1; $mny = $H; $mxy = -1
    for ($y = $y0; $y -le $y1; $y++) {
      $io = $y * $W
      for ($x = $c[0]; $x -le $c[1]; $x++) {
        if ($ink[$io + $x]) {
          if ($x -lt $mnx) { $mnx = $x }
          if ($x -gt $mxx) { $mxx = $x }
          if ($y -lt $mny) { $mny = $y }
          if ($y -gt $mxy) { $mxy = $y }
        }
      }
    }
    $cells.Add([pscustomobject]@{ Row = $b + 1; X = $mnx; Y = $mny; W = $mxx - $mnx + 1; H = $mxy - $mny + 1 })
  }
}

if ($cells.Count -ne $Names.Count) { throw "segmented $($cells.Count) icons but got $($Names.Count) names" }

# Alpha is recovered by un-premultiplying against the black background. Each palette colour peaks
# at a different brightness (navy at 99, cyan at 222), so one luminance threshold would render
# navy at 39% opacity; matching chromaticity first gives the right divisor.
$pal = @(
  @{ N='navy';    R=22;  G=10;  B=99  },
  @{ N='cyan';    R=131; G=210; B=222 },
  @{ N='magenta'; R=222; G=40;  B=192 },
  @{ N='plum';    R=78;  G=40;  B=94  }
)
foreach ($p in $pal) {
  $m = [Math]::Max([Math]::Max($p.R, $p.G), $p.B)
  $p.M = $m; $p.NR = $p.R / $m; $p.NG = $p.G / $m; $p.NB = $p.B / $m
}

if (-not $ReportOnly) { New-Item -ItemType Directory -Force -Path $OutDir | Out-Null }

for ($i = 0; $i -lt $cells.Count; $i++) {
  $c = $cells[$i]; $name = $Names[$i]
  Write-Output ("{0,-28} row{1} x={2,4} y={3,4} w={4,4} h={5,4}" -f $name, $c.Row, $c.X, $c.Y, $c.W, $c.H)
  if ($ReportOnly -or $name -eq '-') { continue }

  # Downscale on the ORIGINAL black background, then key. Scaling an already-keyed bitmap makes
  # GDI+ interpolate RGB and alpha independently, which fringes every edge; the sheet is already
  # premultiplied against black, and downscaling premultiplied is correct.
  $inner = $Size - 2 * $Pad
  $scale = [Math]::Min($inner / $c.W, $inner / $c.H)
  $dw = [Math]::Max(1, [int][Math]::Round($c.W * $scale))
  $dh = [Math]::Max(1, [int][Math]::Round($c.H * $scale))
  $dx = [int][Math]::Round(($Size - $dw) / 2.0)
  $dy = [int][Math]::Round(($Size - $dh) / 2.0)

  $tmp = New-Object System.Drawing.Bitmap $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($tmp)
  $g.Clear([System.Drawing.Color]::Black)
  $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
  $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
  $src = New-Object System.Drawing.Rectangle $c.X, $c.Y, $c.W, $c.H
  $dst = New-Object System.Drawing.Rectangle $dx, $dy, $dw, $dh
  $g.DrawImage($bmp, $dst, $src, [System.Drawing.GraphicsUnit]::Pixel)
  $g.Dispose()

  $out = New-Object System.Drawing.Bitmap $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  for ($y = 0; $y -lt $Size; $y++) {
    for ($x = 0; $x -lt $Size; $x++) {
      $px = $tmp.GetPixel($x, $y)
      $r = [int]$px.R; $gg = [int]$px.G; $bb = [int]$px.B
      $m = [Math]::Max([Math]::Max($r, $gg), $bb)
      if ($m -le 8) { $out.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(0,0,0,0)); continue }
      $nr = $r / $m; $ng = $gg / $m; $nb = $bb / $m
      $bestP = $null; $bestD = [double]::MaxValue
      foreach ($p in $pal) {
        $d = [Math]::Pow($nr - $p.NR, 2) + [Math]::Pow($ng - $p.NG, 2) + [Math]::Pow($nb - $p.NB, 2)
        if ($d -lt $bestD) { $bestD = $d; $bestP = $p }
      }
      $a = [Math]::Min(1.0, $m / $bestP.M)
      $cr = [Math]::Min(255, [int][Math]::Round($r / $a))
      $cg = [Math]::Min(255, [int][Math]::Round($gg / $a))
      $cb = [Math]::Min(255, [int][Math]::Round($bb / $a))
      $out.SetPixel($x, $y, [System.Drawing.Color]::FromArgb([int][Math]::Round($a * 255), $cr, $cg, $cb))
    }
  }
  $out.Save((Join-Path $OutDir "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
  $out.Dispose(); $tmp.Dispose()
}

$bmp.Dispose()
