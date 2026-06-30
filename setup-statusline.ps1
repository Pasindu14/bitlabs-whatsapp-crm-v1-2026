# Claude Code Statusline Setup Script
# Run on any Windows PC:
#   powershell -ExecutionPolicy Bypass -File setup-statusline.ps1

$ClaudeDir    = "$env:USERPROFILE\.claude"
$ScriptPath   = "$ClaudeDir\statusline-command.sh"
$SettingsPath = "$ClaudeDir\settings.json"

Write-Host ""
Write-Host "Claude Code Statusline Setup" -ForegroundColor Cyan
Write-Host "=============================" -ForegroundColor Cyan
Write-Host ""

# 1. Check Git Bash
$gitBash = "C:\Program Files\Git\bin\bash.exe"
if (-not (Test-Path $gitBash)) {
    $bashCmd = Get-Command bash -ErrorAction SilentlyContinue
    $gitBash = if ($bashCmd) { $bashCmd.Source } else { $null }
}
if (-not $gitBash) {
    Write-Host "[ERROR] Git Bash not found. Install Git for Windows:" -ForegroundColor Red
    Write-Host "        https://git-scm.com/download/win" -ForegroundColor Yellow
    exit 1
}
Write-Host "[OK] Git Bash: $gitBash" -ForegroundColor Green

# 2. Ensure .claude dir exists
if (-not (Test-Path $ClaudeDir)) {
    New-Item -ItemType Directory -Path $ClaudeDir -Force | Out-Null
    Write-Host "[OK] Created $ClaudeDir" -ForegroundColor Green
} else {
    Write-Host "[OK] Claude dir: $ClaudeDir" -ForegroundColor Green
}

# 3. Write the bash statusline script (Base64 encoded to avoid escaping issues)
$b64 = "IyEvdXNyL2Jpbi9lbnYgYmFzaAojIENsYXVkZSBDb2RlIHN0YXR1cyBsaW5lIC0gUG93ZXJsaW5lIHN0eWxlCgppbnB1dD0kKGNhdCkKCiMgTW9kZWwKbW9kZWw9JChlY2hvICIkaW5wdXQiIHwgZ3JlcCAtbyAnImRpc3BsYXlfbmFtZSI6IlteIl0qIicgfCBoZWFkIC0xIHwgc2VkICdzLyJkaXNwbGF5X25hbWUiOiIvLztzLyIvLycpCnRpbWVfbm93PSQoZGF0ZSArJUg6JU06JVMpCgojIENvbnRleHQgd2luZG93IGZpZWxkcwpjdHhfcGN0PSQoZWNobyAiJGlucHV0IiB8IGdyZXAgLW8gJyJ1c2VkX3BlcmNlbnRhZ2UiOlswLTkuXSonIHwgaGVhZCAtMSB8IHNlZCAncy8idXNlZF9wZXJjZW50YWdlIjovLycpCmN0eF9zaXplPSQoZWNobyAiJGlucHV0IiB8IGdyZXAgLW8gJyJjb250ZXh0X3dpbmRvd19zaXplIjpbMC05XSonIHwgaGVhZCAtMSB8IHNlZCAncy8iY29udGV4dF93aW5kb3dfc2l6ZSI6Ly8nKQoKIyBCdWlsZCBjb250ZXh0IGxhYmVsOiAiNDUlIDkway8yMDBrIgpjdHhfbGFiZWw9IiIKaWYgWyAtbiAiJGN0eF9wY3QiIF0gJiYgWyAtbiAiJGN0eF9zaXplIiBdOyB0aGVuCiAgcGN0X2ludD0ke2N0eF9wY3QlLip9CiAgc2l6ZV9rPSQoKGN0eF9zaXplIC8gMTAwMCkpCiAgdXNlZF9rPSQoKCAocGN0X2ludCAqIGN0eF9zaXplKSAvIDEwMCAvIDEwMDAgKSkKICBjdHhfbGFiZWw9IiR7cGN0X2ludH0lICR7dXNlZF9rfWsvJHtzaXplX2t9ayIKICAjIENvbG9yIGJ5IHVzYWdlOiBibHVlIDwgNTAlLCB5ZWxsb3cgNTAtNzQlLCBvcmFuZ2UgNzUtODklLCByZWQgOTAlKwogIGlmICAgWyAiJHBjdF9pbnQiIC1nZSA5MCBdOyB0aGVuIENfQ1RYPTE5NgogIGVsaWYgWyAiJHBjdF9pbnQiIC1nZSA3NSBdOyB0aGVuIENfQ1RYPTIwMgogIGVsaWYgWyAiJHBjdF9pbnQiIC1nZSA1MCBdOyB0aGVuIENfQ1RYPTIxNAogIGVsc2UgQ19DVFg9MjcKICBmaQplbHNlCiAgQ19DVFg9MjcKZmkKCiMgTGFzdCB1c2VyIG1lc3NhZ2UgZnJvbSB0cmFuc2NyaXB0CnRyYW5zY3JpcHQ9JChlY2hvICIkaW5wdXQiIHwgZ3JlcCAtbyAnInRyYW5zY3JpcHRfcGF0aCI6IlteIl0qIicgfCBzZWQgJ3MvInRyYW5zY3JpcHRfcGF0aCI6Ii8vO3MvIiQvLycgfCB0ciAnXCcgJy8nIHwgc2VkICdzfC8vfC98ZycpCmxhc3RfdXNlcl9tc2c9IiIKaWYgWyAtbiAiJHRyYW5zY3JpcHQiIF0gJiYgWyAtZiAiJHRyYW5zY3JpcHQiIF07IHRoZW4KICByYXc9JChncmVwICcidHlwZSI6Imxhc3QtcHJvbXB0IicgIiR0cmFuc2NyaXB0IiB8IGdyZXAgJyJsYXN0UHJvbXB0IjonIHwgdGFpbCAtMSkKICBpZiBbIC1uICIkcmF3IiBdOyB0aGVuCiAgICBsYXN0X3VzZXJfbXNnPSQoZWNobyAiJHJhdyIgfCBncmVwIC1vICcibGFzdFByb21wdCI6IlteIl0qIicgfCBzZWQgJ3MvImxhc3RQcm9tcHQiOiIvLztzLyIkLy8nKQogICAgbGFzdF91c2VyX21zZz0kKGVjaG8gIiRsYXN0X3VzZXJfbXNnIiB8IHNlZCAncy9cW0ltYWdlICNbMC05XSpcXSAvL2cnIHwgc2VkICdzL1xbSW1hZ2U6IFteXV0qXF0vL2cnIHwgc2VkICdzL15bWzpzcGFjZTpdXSovLycpCiAgICBpZiBbICIkeyNsYXN0X3VzZXJfbXNnfSIgLWd0IDYwIF07IHRoZW4KICAgICAgbGFzdF91c2VyX21zZz0iJHtsYXN0X3VzZXJfbXNnOjA6NjB9Li4uIgogICAgZmkKICBmaQpmaQoKIyBQb3dlcmxpbmUgZmlsbGVkIGFycm93IChVK0UwQjApCkFSUj0kKHByaW50ZiAnJykKCkMxPTIwOCAgIyBvcmFuZ2UgIC0gbW9kZWwKQzI9MjggICAjIGdyZWVuICAgLSBsYXN0IG1lc3NhZ2UKQzM9MTUgICAjIHdoaXRlICAgLSB0aW1lCgpSPSJcMDMzWzBtIgoKc2VnKCkgICB7IHByaW50ZiAiXDAzM1s0ODs1OyR7MX1tXDAzM1szODs1OyR7Mn1tICAlcyAgIiAiJHszfSI7IH0KdHJhbnMoKSB7IHByaW50ZiAiJHtSfVwwMzNbMzg7NTskezF9bVwwMzNbNDg7NTskezJ9bSR7QVJSfSI7IH0KY2FwKCkgICB7IHByaW50ZiAiJHtSfVwwMzNbMzg7NTskezF9bSR7QVJSfSR7Un0iOyB9CgojIFJlbmRlciBzZWdtZW50cwppZiBbIC1uICIkbW9kZWwiIF0gJiYgWyAtbiAiJGN0eF9sYWJlbCIgXSAmJiBbIC1uICIkbGFzdF91c2VyX21zZyIgXTsgdGhlbgogIHNlZyAgJEMxICAgIDAgICAiJG1vZGVsIgogIHRyYW5zICRDMSAgICRDX0NUWAogIHNlZyAgJENfQ1RYIDI1NSAiJGN0eF9sYWJlbCIKICB0cmFucyAkQ19DVFggJEMyCiAgc2VnICAkQzIgICAyNTUgICIkbGFzdF91c2VyX21zZyIKICB0cmFucyAkQzIgICAkQzMKICBzZWcgICRDMyAgICAwICAgIiR0aW1lX25vdyIKICBjYXAgICRDMwplbGlmIFsgLW4gIiRtb2RlbCIgXSAmJiBbIC1uICIkY3R4X2xhYmVsIiBdOyB0aGVuCiAgc2VnICAkQzEgICAgMCAgICIkbW9kZWwiCiAgdHJhbnMgJEMxICAgJENfQ1RYCiAgc2VnICAkQ19DVFggMjU1ICIkY3R4X2xhYmVsIgogIHRyYW5zICRDX0NUWCAkQzMKICBzZWcgICRDMyAgICAwICAgIiR0aW1lX25vdyIKICBjYXAgICRDMwplbGlmIFsgLW4gIiRtb2RlbCIgXTsgdGhlbgogIHNlZyAgJEMxICAwICAiJG1vZGVsIgogIHRyYW5zICRDMSAkQzMKICBzZWcgICRDMyAgMCAgIiR0aW1lX25vdyIKICBjYXAgICRDMwplbHNlCiAgc2VnICRDMyAwICIkdGltZV9ub3ciCiAgY2FwICRDMwpmaQo="
$bytes = [System.Convert]::FromBase64String($b64)
[System.IO.File]::WriteAllBytes($ScriptPath, $bytes)
Write-Host "[OK] Written: $ScriptPath" -ForegroundColor Green

# 4. Merge statusLine into settings.json
$claudeDirFwd  = $ClaudeDir.Replace('\', '/')
$statusLineCmd = "bash $claudeDirFwd/statusline-command.sh"

if (Test-Path $SettingsPath) {
    $json = Get-Content $SettingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
    Write-Host "[OK] Found existing settings.json - merging..." -ForegroundColor Green
} else {
    $json = [PSCustomObject]@{}
    Write-Host "[OK] No settings.json - creating new one..." -ForegroundColor Green
}

$json | Add-Member -MemberType NoteProperty -Name "statusLine" -Value (
    [PSCustomObject]@{ type = "command"; command = $statusLineCmd }
) -Force

$jsonOut = $json | ConvertTo-Json -Depth 10
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($SettingsPath, $jsonOut, $utf8NoBom)
Write-Host "[OK] Updated: $SettingsPath" -ForegroundColor Green

Write-Host ""
Write-Host "Done! Restart Claude Code to see the statusline." -ForegroundColor Cyan
Write-Host ""
Write-Host "  [orange] Model   [blue] Context used   [green] Last message   [white] Time" -ForegroundColor Gray
Write-Host ""
Write-Host "Context segment color:" -ForegroundColor Gray
Write-Host "  Blue < 50%   Yellow 50-74%   Orange 75-89%   Red 90%+" -ForegroundColor Gray
Write-Host ""
Write-Host "Note: Requires a Nerd Font for the Powerline arrows." -ForegroundColor Yellow
Write-Host ""
