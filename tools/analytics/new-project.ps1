# LogKit analytics provisioning: create, register, deploy, verify a game analytics backend.
# Default is a dry run that prints the plan. Add -Apply to execute.
# Windows PowerShell 5.1 compatible. Keys are written only to the secrets folder and deploy appsettings, never to git.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Name,
    [string]$Template = 'C:\Gits\logkit-analytics-template',
    [string]$CoreRepo = 'C:\Gits\logkit-core',
    [string]$ReposRoot = 'C:\Gits',
    [string]$DeployRoot = 'C:\UnityCI\LogAnalytics',
    [string]$IngestDir = 'C:\UnityCI\LogServer',
    [int]$IngestPort = 5000,
    [string]$IngestHost = '172.21.10.67',
    [string]$RegistryPath = 'C:\UnityCI\analytics-registry.json',
    [string]$SecretsDir = 'C:\UnityCI\secrets',
    [switch]$Apply,
    [switch]$RefreshSecrets
)

$ErrorActionPreference = 'Continue'   # native git writes progress to stderr; failures are checked with `$LASTEXITCODE and throw
$Utf8 = New-Object System.Text.UTF8Encoding $false

function Write-Step([string]$text) { Write-Host ('== ' + $text) -ForegroundColor Cyan }

function New-Key {
    $bytes = New-Object byte[] 24
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    return ([Convert]::ToBase64String($bytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function Read-Json([string]$path) {
    return [System.IO.File]::ReadAllText($path, $Utf8) | ConvertFrom-Json
}

function Write-Json([string]$path, $value) {
    $json = $value | ConvertTo-Json -Depth 10
    [System.IO.File]::WriteAllText($path, $json, $Utf8)
}

function Test-PortListening([int]$port) {
    return [bool](Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue)
}

function Stop-Port([int]$port) {
    $owners = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess -Unique
    foreach ($ownerPid in $owners) {
        if ($ownerPid -and $ownerPid -ne $PID) { Stop-Process -Id $ownerPid -Force; Write-Host ('stopped pid ' + $ownerPid + ' on port ' + $port) }
    }
    for ($i = 0; $i -lt 20 -and (Test-PortListening $port); $i++) { Start-Sleep -Milliseconds 500 }
}

function Start-Launcher([string]$dir) {
    $vbs = Join-Path $dir 'launch.vbs'
    if (-not (Test-Path $vbs)) { throw ('launcher not found: ' + $vbs) }
    Start-Process -FilePath 'wscript.exe' -ArgumentList @('//B', ('"' + $vbs + '"')) | Out-Null
}

function Wait-Json([string]$url, [hashtable]$headers, [int]$seconds, [scriptblock]$ok) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $body = Invoke-RestMethod -Uri $url -Headers $headers -TimeoutSec 4
            if (& $ok $body) { return $body }
        } catch { }
        Start-Sleep -Seconds 2
    }
    return $null
}

# Writes the secrets file: every address and key a user needs for LogKit, with where each key goes.
# Lines starting with KEY= are machine-readable (used by -RefreshSecrets); keep them exact.
function Write-Secrets([string]$path, [string]$projectName, [int]$analyticsPort, [string]$ingestHost, [int]$ingestPort, [string]$analyticsKey, [string]$uploadKey, [string]$readKey) {
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add('# ' + $projectName + ' analytics - addresses and keys (do not share, do not commit)')
    $lines.Add('')
    $lines.Add('## Addresses')
    $lines.Add('GM tool (session logs)          : http://' + $ingestHost + ':' + $ingestPort + '/dashboard')
    $lines.Add('Analytics page (' + $projectName + ')  : http://' + $ingestHost + ':' + $analyticsPort + '/analytics')
    $lines.Add('Game log upload (GameLogger)    : http://' + $ingestHost + ':' + $ingestPort + '/api/logs')
    $lines.Add('Ingest health                   : http://' + $ingestHost + ':' + $ingestPort + '/health')
    $lines.Add('Analytics health                : http://' + $ingestHost + ':' + $analyticsPort + '/health')
    $lines.Add('')
    $lines.Add('## Keys (value on the line after the label; the KEY= lines are read by tooling)')
    $lines.Add('# READ: GM tool "API 키" box. Views all projects. Cannot delete. Shared across projects.')
    $lines.Add('READ_KEY=' + $readKey)
    $lines.Add('# ANALYTICS_KEY: this project''s analytics page "API 키" box only. Not accepted by the GM tool.')
    $lines.Add('ANALYTICS_KEY=' + $analyticsKey)
    $lines.Add('# UPLOAD_KEY: game build GameLogger "Api Key" only. Cannot view anything.')
    $lines.Add('UPLOAD_KEY=' + $uploadKey)
    $lines.Add('')
    $lines.Add('## Where to type what')
    $lines.Add('- GameLogger (game build)      -> UPLOAD_KEY')
    $lines.Add('- GM tool login                -> READ_KEY')
    $lines.Add('- ' + $projectName + ' analytics page login -> ANALYTICS_KEY')
    [System.IO.File]::WriteAllText($path, (($lines -join "`r`n") + "`r`n"), $Utf8)
}

# ---------- 1. Plan ----------
# -cnotmatch: case-sensitive. The ingest server accepts lowercase names only.
if ($Name -cnotmatch '^[a-z0-9][a-z0-9_-]{0,31}$') {
    throw 'Name must be lowercase letters, digits, _ or -, up to 32 characters.'
}

# Read-only here. The registry is created or updated only with -Apply.
$registry = if (Test-Path $RegistryPath) { Read-Json $RegistryPath } else { [PSCustomObject]@{ ingest = [PSCustomObject]@{ port = $IngestPort; dir = $IngestDir }; projects = @() } }
$projects = @($registry.projects)
if (-not $RefreshSecrets -and ($projects | Where-Object { $_.name -eq $Name })) { throw ('project already registered: ' + $Name + ' (use -RefreshSecrets to rewrite its secrets file)') }

$maxPort = $IngestPort
foreach ($p in $projects) { if ([int]$p.port -gt $maxPort) { $maxPort = [int]$p.port } }
$port = $maxPort + 1
while ((Test-PortListening $port) -or ($projects | Where-Object { [int]$_.port -eq $port })) { $port++ }

# Refresh mode reuses the port recorded in the registry, not a new one.
if ($RefreshSecrets) {
    $registered = $projects | Where-Object { $_.name -eq $Name } | Select-Object -First 1
    if ($registered) { $port = [int]$registered.port }
}
$repo = Join-Path $ReposRoot ('logkit-analytics-' + $Name)
$deploy = Join-Path $DeployRoot $Name
$secretsFile = Join-Path $SecretsDir ($Name + '.txt')
if (-not $RefreshSecrets -and (Test-Path $repo)) { throw ('repo folder already exists: ' + $repo) }
if (-not $RefreshSecrets -and (Test-Path $deploy)) { throw ('deploy folder already exists: ' + $deploy) }

Write-Host ''
Write-Host ('Project      : ' + $Name)
Write-Host ('Port         : ' + $port)
Write-Host ('Repo         : ' + $repo + '  (from ' + $Template + ', core ' + $CoreRepo + ')')
Write-Host ('Deploy       : ' + $deploy)
Write-Host ('Ingest       : ' + $IngestDir + ' port ' + $IngestPort + ' (add Projects["' + $Name + '"] and restart)')
Write-Host ('Analytics URL: http://' + $IngestHost + ':' + $port + '/analytics')
Write-Host ('Secrets      : ' + $secretsFile)
Write-Host ('Registry     : ' + $RegistryPath)
if ($RefreshSecrets) {
    $entry = $projects | Where-Object { $_.name -eq $Name } | Select-Object -First 1
    if (-not $entry) { throw ('not registered: ' + $Name) }
    if (-not (Test-Path $secretsFile)) { throw ('secrets file not found: ' + $secretsFile) }
    $oldKeys = @{}
    foreach ($line in (Get-Content $secretsFile)) {
        if ($line -cmatch '^(ANALYTICS_KEY|UPLOAD_KEY)=(.*)$') { $oldKeys[$matches[1]] = $matches[2] }
    }
    if (-not $oldKeys['ANALYTICS_KEY'] -or -not $oldKeys['UPLOAD_KEY']) { throw 'existing keys not found in the secrets file; refusing to rewrite' }
    $ingestCfg = Read-Json (Join-Path $IngestDir 'appsettings.json')
    $readKey = if ($ingestCfg.ReadApiKey) { [string]$ingestCfg.ReadApiKey } else { [string]$ingestCfg.ApiKey }
    Write-Secrets $secretsFile $Name ([int]$entry.port) $IngestHost $IngestPort $oldKeys['ANALYTICS_KEY'] $oldKeys['UPLOAD_KEY'] $readKey
    Write-Host ('secrets file rewritten (keys kept): ' + $secretsFile) -ForegroundColor Green
    return
}

Write-Host ''

if (-not $Apply) {
    Write-Host 'Dry run. Add -Apply to execute.' -ForegroundColor Yellow
    return
}

# ---------- 2. Repository ----------
Write-Step 'create repository from template'
git clone -q $Template $repo
if ($LASTEXITCODE -ne 0) { throw 'git clone failed' }
Push-Location $repo
try {
    # Local override only (.git/config). The committed .gitmodules keeps the GitHub URL.
    git config submodule.core.url $CoreRepo
    git -c protocol.file.allow=always submodule update -q --init
    if ($LASTEXITCODE -ne 0) { throw 'core submodule update failed (check CoreRepo and the pinned commit)' }

    # ---------- 3. Keys ----------
    Write-Step 'create keys'
    $ingestCfgPath = Join-Path $IngestDir 'appsettings.json'
    $ingestCfg = Read-Json $ingestCfgPath
    $readKey = if ($ingestCfg.ReadApiKey) { [string]$ingestCfg.ReadApiKey } else { [string]$ingestCfg.ApiKey }
    $analyticsKey = New-Key
    $uploadKey = New-Key

    # ---------- 4. Analytics config and build ----------
    Write-Step 'publish analytics server'
    New-Item -ItemType Directory -Path $deploy | Out-Null
    dotnet publish (Join-Path $repo 'AnalyticsServer.csproj') -c Release -nologo -v q -o $deploy
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

    $analyticsCfg = [ordered]@{
        Urls = 'http://0.0.0.0:' + $port
        ApiKey = $analyticsKey
        IngestUrl = 'http://localhost:' + $IngestPort
        IngestApiKey = $readKey
        Project = $Name
        GmToolUrl = 'http://' + $IngestHost + ':' + $IngestPort + '/dashboard'
        SyncIntervalSeconds = 60
        IngestTimeoutSeconds = 120
        DataPath = (Join-Path $deploy 'data')
        KeepRawJsonl = $false
    }
    Write-Json (Join-Path $deploy 'appsettings.json') $analyticsCfg

    $vbsLines = @(
        "' $Name analytics server (hidden launcher)",
        'Set sh = CreateObject("WScript.Shell")',
        ('sh.CurrentDirectory = "' + $deploy + '"'),
        ('sh.Run """' + (Join-Path $deploy 'AnalyticsServer.exe') + '""", 0, False')
    )
    [System.IO.File]::WriteAllText((Join-Path $deploy 'launch.vbs'), (($vbsLines -join "`r`n") + "`r`n"), [System.Text.Encoding]::ASCII)

    # ---------- 5. Register in the ingest server ----------
    Write-Step 'register project in ingest server'
    $backup = Join-Path $IngestDir ('appsettings.' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.bak')
    Copy-Item $ingestCfgPath $backup
    if (-not $ingestCfg.PSObject.Properties['Projects']) {
        Add-Member -InputObject $ingestCfg -NotePropertyName Projects -NotePropertyValue (New-Object PSObject)
    }
    if ($ingestCfg.Projects.PSObject.Properties[$Name]) { throw ('ingest already has project ' + $Name) }
    Add-Member -InputObject $ingestCfg.Projects -NotePropertyName $Name -NotePropertyValue $uploadKey
    Write-Json $ingestCfgPath $ingestCfg
    Stop-Port $IngestPort
    Start-Launcher $IngestDir
    $ingestHealth = Wait-Json ('http://localhost:' + $IngestPort + '/health') @{} 60 { param($b) $b -match 'ok' }
    if (-not $ingestHealth) {
        # Roll back: restore the config, start the ingest server again, then stop.
        Write-Host 'ingest did not come back; restoring the backup config and restarting it' -ForegroundColor Yellow
        Stop-Port $IngestPort
        Copy-Item $backup $ingestCfgPath -Force
        Start-Launcher $IngestDir
        $restored = Wait-Json ('http://localhost:' + $IngestPort + '/health') @{} 60 { param($b) $b -match 'ok' }
        throw ('ingest server failed with the new project entry. Config restored from ' + $backup + ' (ingest back: ' + [bool]$restored + ')')
    }

    # ---------- 6. Start analytics and verify ----------
    Write-Step 'start analytics server'
    Start-Launcher $deploy
    $health = Wait-Json ('http://localhost:' + $port + '/health') @{} 60 { param($b) $b -match 'ok' }
    if (-not $health) { throw ('analytics server did not start on port ' + $port) }
    $mirror = Wait-Json ('http://localhost:' + $port + '/api/mirror/status') @{ 'X-Api-Key' = $analyticsKey } 90 { param($b) $b.ok -eq $true }
    if (-not $mirror) { throw 'mirror did not sync (check IngestUrl and the read key)' }
    $unauth = try { (Invoke-WebRequest -Uri ('http://localhost:' + $port + '/api/mirror/status') -UseBasicParsing -TimeoutSec 4).StatusCode } catch { $_.Exception.Response.StatusCode.value__ }
    if ($unauth -ne 401) { throw ('analytics API accepted a request without a key (status ' + $unauth + ')') }

    # ---------- 7. Firewall, secrets, registry, commit ----------
    Write-Step 'firewall rule'
    try {
        netsh advfirewall firewall add rule name=("LogKit Analytics " + $Name) dir=in action=allow protocol=TCP localport=$port | Out-Null
    } catch {
        Write-Host 'firewall rule not added (run as administrator to open the port)' -ForegroundColor Yellow
    }

    Write-Step 'save secrets and registry'
    if (-not (Test-Path $SecretsDir)) { New-Item -ItemType Directory -Path $SecretsDir | Out-Null }
    Write-Secrets $secretsFile $Name $port $IngestHost $IngestPort $analyticsKey $uploadKey $readKey

    $entry = [ordered]@{ name = $Name; port = $port; repo = $repo; deploy = $deploy; secrets = $secretsFile; createdUtc = (Get-Date).ToUniversalTime().ToString('o') }
    $registry.projects = @($registry.projects) + @([PSCustomObject]$entry)
    Write-Json $RegistryPath $registry

    git add -A
    if (git status --porcelain) {
        git commit -q -m ("chore: " + $Name + " 분석 서버 생성 (템플릿 기반)")
        Write-Host ('repo committed (not pushed): ' + $repo)
    } else {
        Write-Host 'repo has no changes to commit (template state)'
    }
} finally {
    Pop-Location
}

Write-Host ''
Write-Host ('Done. Analytics page: http://' + $IngestHost + ':' + $port + '/analytics') -ForegroundColor Green
Write-Host ('Keys are in ' + $secretsFile + ' (ANALYTICS_KEY for the page, UPLOAD_KEY for the game build).')
