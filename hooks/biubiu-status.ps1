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

    function Get-TasksPath {
        if (-not [string]::IsNullOrWhiteSpace($env:BIUBIU_TASKS_PATH)) {
            $parent = Split-Path -Parent $env:BIUBIU_TASKS_PATH
            if (-not [string]::IsNullOrWhiteSpace($parent)) {
                [System.IO.Directory]::CreateDirectory($parent) | Out-Null
            }
            return $env:BIUBIU_TASKS_PATH
        }
        $dir = Join-Path $env:LOCALAPPDATA "Biubiu"
        [System.IO.Directory]::CreateDirectory($dir) | Out-Null
        return (Join-Path $dir "tasks.json")
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

        $path = Get-TasksPath
        $mutex = New-Object System.Threading.Mutex($false, "Local\BiubiuCursorTasks")
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

            $tasks = New-Object System.Collections.Generic.List[object]
            if (Test-Path -LiteralPath $path) {
                $existingText = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
                if (-not [string]::IsNullOrWhiteSpace($existingText)) {
                    $parsed = $existingText | ConvertFrom-Json
                    if ($parsed -ne $null -and $parsed.tasks -ne $null) {
                        foreach ($item in @($parsed.tasks)) {
                            $tasks.Add(@{
                                ConversationId = [string]$item.conversationId
                                WorkspacePath = [string]$item.workspacePath
                                Summary = [string]$item.summary
                                Status = [string]$item.status
                                UpdatedAt = [string]$item.updatedAt
                            })
                        }
                    }
                }
            }

            $now = [DateTimeOffset]::Now.ToString("o")
            $found = $false
            foreach ($item in $tasks) {
                if ($item.ConversationId -eq $conversationId) {
                    $found = $true
                    $item.WorkspacePath = $workspace
                    $item.Status = $status
                    $item.UpdatedAt = $now
                    if ($EventName -eq "beforeSubmitPrompt") {
                        $item.Summary = $summary
                    }
                }
            }
            if (-not $found) {
                $tasks.Add(@{
                    ConversationId = $conversationId
                    WorkspacePath = $workspace
                    Summary = $summary
                    Status = $status
                    UpdatedAt = $now
                })
            }

            while ($tasks.Count -gt 50) {
                $oldest = 0
                $oldestStamp = [string]$tasks[0].UpdatedAt
                for ($i = 1; $i -lt $tasks.Count; $i++) {
                    $stamp = [string]$tasks[$i].UpdatedAt
                    if ($stamp -lt $oldestStamp) {
                        $oldest = $i
                        $oldestStamp = $stamp
                    }
                }
                $tasks.RemoveAt($oldest)
            }

            $json = ConvertTo-TaskJson $tasks
            $utf8 = New-Object System.Text.UTF8Encoding $false
            $temp = $path + ".tmp"
            [System.IO.File]::WriteAllText($temp, $json, $utf8)
            [System.IO.File]::Copy($temp, $path, $true)
            [System.IO.File]::Delete($temp)
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
