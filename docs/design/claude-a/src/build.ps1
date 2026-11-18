$src = $PSScriptRoot
$out = Split-Path $src
$enc = New-Object System.Text.UTF8Encoding($false)
function Read-Src($p) { [IO.File]::ReadAllText((Join-Path $src $p), $enc) }
$parts = @{ CSS = Read-Src 'shared.css'; JS = Read-Src 'shared.js'; DEFS = Read-Src 'p-DEFS.html' }
foreach ($m in [regex]::Matches((Read-Src 'p-PARTS.html'), '(?ms)^@@(\w+)\r?\n(.*?)(?=^@@|\z)')) {
  $parts[$m.Groups[1].Value] = $m.Groups[2].Value.TrimEnd()
}
foreach ($n in 'a', 'b', 'c') {
  $t = Read-Src "$n.html"
  foreach ($k in $parts.Keys) { $t = $t.Replace("<!--P:$k-->", $parts[$k]).Replace("/*P:$k*/", $parts[$k]) }
  $f = Join-Path $out "konsept-$n.html"
  [IO.File]::WriteAllText($f, $t, $enc)
  "{0}  {1:N0} bayt" -f (Split-Path $f -Leaf), (Get-Item $f).Length
}
