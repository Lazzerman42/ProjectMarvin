param([string]$BaseUrl = 'http://192.168.1.20:4200')

$ErrorActionPreference = 'Stop'
$stamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'

# 1. Reachable? (Echo returns what you send)
$echo = Invoke-RestMethod "$BaseUrl/api/Echo/ping"
if ($echo -ne 'ping') { throw "Echo returned '$echo'" }
Write-Host "OK  Echo"

# 2. Server time
Write-Host "OK  Server time: $(Invoke-RestMethod "$BaseUrl/api/GetLocalTime")"

# 3. Database found?
$before = [int]((Invoke-RestMethod "$BaseUrl/api/LogCount") -replace '\D')
Write-Host "OK  Database, $before entries"

# 4. Log entry via JSON POST (the modern endpoint)
$body = @{ Sender = 'Deploy check'; LogType = 'Info'; Message = "Marvin API verified $stamp (POST, åäö)" } | ConvertTo-Json
$saved = Invoke-RestMethod -Method Post "$BaseUrl/api/LogEx/" -ContentType 'application/json; charset=utf-8' -Body $body
Write-Host "OK  POST api/LogEx, saved from $($saved.ipAdress)"

# 5. Log entry via plain GET (what the simplest sensors use)
$null = Invoke-RestMethod "$BaseUrl/api/Log/$([uri]::EscapeDataString("Marvin API verified $stamp (GET)"))"
Write-Host "OK  GET api/Log"

# 6. Both saved?
$after = [int]((Invoke-RestMethod "$BaseUrl/api/LogCount") -replace '\D')
if ($after -lt $before + 2) { throw "Expected at least $($before + 2) entries, found $after" }
Write-Host "OK  Database now has $after entries - look for 'Deploy check' in MarvinWeb" -ForegroundColor Green
