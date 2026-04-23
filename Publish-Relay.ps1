# Publish-Relay.ps1
# Builds self-contained single-file relay executables for Windows and Linux.
# Output lands in: dist/relay/

$project = "$PSScriptRoot\src\SemaBuzz.Relay\SemaBuzz.Relay.csproj"
$outRoot  = "$PSScriptRoot\dist\relay"

$targets = @(
    @{ RID = "win-x64";   Name = "SemaBuzz-Relay-Windows.exe" },
    @{ RID = "linux-x64"; Name = "SemaBuzz-Relay-Linux" }
)

foreach ($t in $targets) {
    $outDir = "$outRoot\$($t.RID)"
    Write-Host "Publishing $($t.RID)..." -ForegroundColor Cyan

    dotnet publish $project `
        -c Release `
        -r $t.RID `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -o $outDir

    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAILED for $($t.RID)" -ForegroundColor Red
        exit 1
    }

    # Rename the output binary to the friendly name
    $src = Get-ChildItem $outDir -Filter "SemaBuzz.Relay*" |
           Where-Object { $_.Extension -in @(".exe", "") -or $_.Name -eq "SemaBuzz.Relay" } |
           Select-Object -First 1

    if ($src) {
        $dest = "$outRoot\$($t.Name)"
        Copy-Item $src.FullName $dest -Force
        Write-Host "  -> $dest" -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "Done. Relay binaries are in: $outRoot" -ForegroundColor Green
Write-Host ""
Write-Host "Usage:"
Write-Host "  Windows:  SemaBuzz-Relay-Windows.exe [--port 7171]"
Write-Host "  Linux:    ./SemaBuzz-Relay-Linux [--port 7171]"
Write-Host "  Docker:   docker build -t semabuzz-relay . && docker run -p 7171:7171 semabuzz-relay"
