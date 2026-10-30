// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Cli.Updates
{
    internal static class UpdateScript
    {
        public const string Content = """
            param(
                [int]$ParentProcessId,
                [string]$TargetPath,
                [string]$StagedPath,
                [string]$BackupPath,
                [string]$LogPath,
                [string]$ExpectedSha256,
                [string]$Version
            )
            $ErrorActionPreference = "Stop"
            $encoding = New-Object System.Text.UTF8Encoding($false)
            try {
                [System.IO.File]::AppendAllText($LogPath, "Preparing update to $Version." + [Environment]::NewLine, $encoding)
                [Console]::Out.WriteLine("ready")
                [Console]::Out.Flush()
                $parent = Get-Process -Id $ParentProcessId -ErrorAction SilentlyContinue
                if ($null -ne $parent) {
                    if (-not $parent.WaitForExit(60000)) {
                        throw "The application did not exit within sixty seconds."
                    }
                }
                $stream = [System.IO.File]::OpenRead($StagedPath)
                $algorithm = [System.Security.Cryptography.SHA256]::Create()
                try {
                    $actualHash = [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace("-", "")
                }
                finally {
                    $algorithm.Dispose()
                    $stream.Dispose()
                }
                if ($actualHash -ne $ExpectedSha256) {
                    throw "SHA-256 verification failed before installation."
                }
                [System.IO.File]::Replace($StagedPath, $TargetPath, $BackupPath)
                [System.IO.File]::AppendAllText($LogPath, "Updated successfully to $Version." + [Environment]::NewLine, $encoding)
                [System.IO.File]::Delete($BackupPath)
            }
            catch {
                $failure = $_.Exception.ToString()
                if (-not [System.IO.File]::Exists($TargetPath) -and [System.IO.File]::Exists($BackupPath)) {
                    [System.IO.File]::Move($BackupPath, $TargetPath)
                }
                [System.IO.File]::AppendAllText($LogPath, "Update failed: $failure" + [Environment]::NewLine, $encoding)
                exit 1
            }
            finally {
                [System.IO.File]::Delete($StagedPath)
                [System.IO.File]::Delete($PSCommandPath)
            }
            """;
    }
}
