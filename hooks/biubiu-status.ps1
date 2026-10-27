param(
    [Parameter(Position = 0)]
    [string]$HookEvent,
    [Parameter(ValueFromPipeline = $true)]
    [string]$PipedInput
)

begin {
    $ErrorActionPreference = "Stop"
    $ProgressPreference = "SilentlyContinue"
    $buffer = New-Object System.Collections.Generic.List[string]

    function Write-HookResult([string]$EventName) {
        $json = "{}"
        if ($EventName -eq "beforeSubmitPrompt") {
            $json = '{"continue":true}'
        }
        $bytes = [System.Text.Encoding]::ASCII.GetBytes($json + "`n")
        $stdout = [Console]::OpenStandardOutput()
        $stdout.Write($bytes, 0, $bytes.Length)
        $stdout.Flush()
    }

    function Convert-WorkspacePath([string]$path) {
        if ([string]::IsNullOrWhiteSpace($path)) {
            return ""
        }
        $path = $path.Trim()
        if ($path -match '^/([A-Za-z]):/(.*)$') {
            $path = $Matches[1] + ':\' + ($Matches[2] -replace '/', '\')
        } else {
            $path = $path.Replace('/', '\')
        }
        if ($path.Length -gt 3) {
            $path = $path.TrimEnd('\')
        }
        return $path
    }

    function Get-TasksDirectory {
        if (-not [string]::IsNullOrWhiteSpace($env:BIUBIU_TASKS_PATH)) {
            $dir = $env:BIUBIU_TASKS_PATH
        } else {
            $dir = Join-Path $env:LOCALAPPDATA "Biubiu\tasks"
        }
        [System.IO.Directory]::CreateDirectory($dir) | Out-Null
        return $dir
    }

    function Get-TaskFileName([string]$conversationId) {
        $safe = [regex]::Replace($conversationId, "[^A-Za-z0-9._-]", "_")
        if ([string]::IsNullOrWhiteSpace($safe)) {
            $safe = "task"
        }
        if ($safe.Length -gt 80) {
            $safe = $safe.Substring(0, 80)
        }
        return $safe + ".json"
    }

    function Read-TaskText([string]$path) {
        if (-not [System.IO.File]::Exists($path)) {
            return ""
        }
        $share = [System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete
        $stream = New-Object System.IO.FileStream($path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, $share)
        try {
            $reader = New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::UTF8)
            return $reader.ReadToEnd()
        } finally {
            $stream.Dispose()
        }
    }

    function Write-TaskFile([string]$path, [string]$json) {
        $utf8 = New-Object System.Text.UTF8Encoding $false
        $temp = $path + ".tmp"
        [System.IO.File]::WriteAllText($temp, $json, $utf8)
        if ([System.IO.File]::Exists($path)) {
            $backup = $path + ".bak"
            [System.IO.File]::Replace($temp, $path, $backup)
            if ([System.IO.File]::Exists($backup)) {
                [System.IO.File]::Delete($backup)
            }
        } else {
            [System.IO.File]::Move($temp, $path)
        }
    }

    function Trim-TaskFiles([string]$dir) {
        $files = @(Get-ChildItem -LiteralPath $dir -Filter *.json -File -ErrorAction SilentlyContinue)
        if ($files.Count -le 50) {
            return
        }
        $ranked = New-Object System.Collections.Generic.List[object]
        foreach ($file in $files) {
            $stamp = ""
            try {
                $parsed = (Read-TaskText $file.FullName) | ConvertFrom-Json
                if ($parsed -ne $null) {
                    $stamp = [string]$parsed.updatedAt
                }
            } catch {
                $stamp = ""
            }
            $ranked.Add(@{ Path = $file.FullName; Stamp = $stamp })
        }
        $ordered = @($ranked | Sort-Object { $_.Stamp })
        $extra = $ordered.Count - 50
        for ($i = 0; $i -lt $extra; $i++) {
            Remove-Item -LiteralPath $ordered[$i].Path -Force -ErrorAction SilentlyContinue
        }
    }

    function Get-Summary([string]$prompt) {
        if ([string]::IsNullOrWhiteSpace($prompt)) {
            return ""
        }
        $text = [regex]::Replace($prompt, "\s+", " ").Trim()
        if ($text.Length -gt 80) {
            $text = $text.Substring(0, 80)
        }
        return $text
    }

    function Escape-JsonText([string]$text) {
        if ($null -eq $text) {
            return ""
        }
        return $text.Replace('\', '\\').Replace('"', '\"').Replace("`r", '\r').Replace("`n", '\n').Replace("`t", '\t')
    }

    function ConvertTo-TaskJson($records) {
        $parts = New-Object System.Collections.Generic.List[string]
        foreach ($item in $records) {
            [void]$parts.Add(
                '{"conversationId":"' + (Escape-JsonText $item.ConversationId) +
                '","workspacePath":"' + (Escape-JsonText $item.WorkspacePath) +
                '","summary":"' + (Escape-JsonText $item.Summary) +
                '","status":"' + (Escape-JsonText $item.Status) +
                '","startedAt":"' + (Escape-JsonText $item.StartedAt) +
                '","updatedAt":"' + (Escape-JsonText $item.UpdatedAt) + '"}')
        }
        return '{"tasks":[' + ($parts -join ",") + ']}'
    }

    function Update-TaskFile([string]$raw, [string]$EventName) {
        $payload = $raw | ConvertFrom-Json
        if ($payload -eq $null) {
            return
        }

        $conversationId = [string]$payload.conversation_id
        if ([string]::IsNullOrWhiteSpace($conversationId)) {
            $conversationId = "missing-" + [DateTimeOffset]::Now.ToString("yyyyMMddHHmmssfff")
        }

        $workspace = ""
        if ($payload.workspace_roots -ne $null) {
            foreach ($root in @($payload.workspace_roots)) {
                $workspace = [string]$root
                break
            }
        }
        $workspace = Convert-WorkspacePath $workspace

        $status = "running"
        $summary = ""
        if ($EventName -eq "stop") {
            $status = [string]$payload.status
            if ($status -ne "completed" -and $status -ne "aborted" -and $status -ne "error") {
                $status = "error"
            }
        } elseif ($payload.prompt -ne $null) {
            $summary = Get-Summary ([string]$payload.prompt)
        }

        $path = Join-Path (Get-TasksDirectory) (Get-TaskFileName $conversationId)
        $mutexName = "Local\BiubiuTask-" + (Get-TaskFileName $conversationId)
        $mutex = New-Object System.Threading.Mutex($false, $mutexName)
        $locked = $false
        try {
            try {
                $locked = $mutex.WaitOne(2000)
            } catch [System.Threading.AbandonedMutexException] {
                $locked = $true
            }
            if (-not $locked) {
                return
            }

            $existingSummary = ""
            $existingStarted = ""
            $existingText = Read-TaskText $path
            if (-not [string]::IsNullOrWhiteSpace($existingText)) {
                $parsed = $existingText | ConvertFrom-Json
                if ($parsed -ne $null) {
                    $existingSummary = [string]$parsed.summary
                    $existingStarted = [string]$parsed.startedAt
                }
            }

            $now = [DateTimeOffset]::Now.ToString("o")
            $startedAt = $existingStarted
            if ($EventName -eq "beforeSubmitPrompt") {
                $startedAt = $now
            } elseif ([string]::IsNullOrWhiteSpace($startedAt)) {
                $startedAt = ""
            }
            if ($EventName -ne "beforeSubmitPrompt") {
                $summary = $existingSummary
            }

            $json = '{"conversationId":"' + (Escape-JsonText $conversationId) +
                '","workspacePath":"' + (Escape-JsonText $workspace) +
                '","summary":"' + (Escape-JsonText $summary) +
                '","status":"' + (Escape-JsonText $status) +
                '","startedAt":"' + (Escape-JsonText $startedAt) +
                '","updatedAt":"' + (Escape-JsonText $now) + '"}'
            Write-TaskFile $path $json
            Trim-TaskFiles (Get-TasksDirectory)
        } finally {
            if ($locked) {
                $mutex.ReleaseMutex()
            }
            $mutex.Dispose()
        }
    }
}

process {
    if ($null -ne $PipedInput -and $PipedInput -ne "") {
        $buffer.Add($PipedInput)
    }
}

end {
    try {
        $raw = $buffer -join "`n"
        if ([string]::IsNullOrWhiteSpace($raw) -and [Console]::IsInputRedirected) {
            $raw = [Console]::In.ReadToEnd()
        }
        if (-not [string]::IsNullOrWhiteSpace($raw)) {
            Update-TaskFile $raw $HookEvent
        }
    } catch {
        if ($_.Exception -ne $null) {
            [Console]::Error.WriteLine($_.Exception.Message)
        }
    } finally {
        Write-HookResult $HookEvent
    }
}
