param(
    [Parameter(Mandatory = $true)][string]$PhoneIp,
    [Parameter(Mandatory = $true)][string]$Token,
    [int]$Port = 8080,
    [string]$TestNumber = "",
    [string]$Message = "SMS Gateway Test"
)

$base = "http://${PhoneIp}:${Port}"
$headers = @{ Authorization = "Bearer $Token" }

Write-Host "GET $base/api/gateway/status"
$status = Invoke-RestMethod -Method GET -Uri "$base/api/gateway/status" -Headers $headers
$status | ConvertTo-Json -Depth 5

if ([string]::IsNullOrWhiteSpace($TestNumber)) {
    Write-Host "Status OK. Pass -TestNumber 9477XXXXXXX to send one SMS."
    exit 0
}

$body = @{
    requestId   = "TEST-$(Get-Date -Format 'yyyyMMddHHmmss')"
    phoneNumber = $TestNumber
    message     = $Message
} | ConvertTo-Json

Write-Host "POST $base/api/sms/send"
$send = Invoke-RestMethod -Method POST -Uri "$base/api/sms/send" -Headers $headers -ContentType "application/json" -Body $body
$send | ConvertTo-Json -Depth 5
