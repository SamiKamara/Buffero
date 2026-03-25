param(
    [switch]$PrintOnly
)

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$binRoot = Join-Path $repoRoot "Buffero.App\bin"

if (-not (Test-Path $binRoot))
{
    Write-Error "Buffero build output directory was not found: $binRoot"
    exit 1
}

$candidates = Get-ChildItem -Path $binRoot -Recurse -Filter "Buffero.App.exe" -File |
    Where-Object {
        $_.FullName -notmatch "\\(publish|ref|refint|win-x64)\\"
    } |
    Sort-Object -Property LastWriteTimeUtc, FullName -Descending

if ($candidates.Count -eq 0)
{
    Write-Error "No runnable Buffero build was found under $binRoot"
    exit 1
}

$selected = $candidates[0]

if ($PrintOnly)
{
    [pscustomobject]@{
        SelectedPath         = $selected.FullName
        SelectedLastWriteUtc = $selected.LastWriteTimeUtc
        Candidates           = ($candidates | Select-Object -ExpandProperty FullName)
    } | Format-List
    return
}

if ($args.Count -gt 0)
{
    Start-Process -FilePath $selected.FullName -WorkingDirectory $selected.DirectoryName -ArgumentList $args
}
else
{
    Start-Process -FilePath $selected.FullName -WorkingDirectory $selected.DirectoryName
}
