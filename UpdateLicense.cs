// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mediana.Tools
{
    internal class UpdateLicense
    {
        private const int StartYear = 2026;
        private const string Author = "Vadim Belov <https://belov.us>";
        private const string Spdx = "MIT";
        private static readonly Encoding FileEncoding = new UTF8Encoding(false);
        private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
        {
            ".git", ".vs", "bin", "obj", "notes", "work", "artifacts", "TestResults", ".idea"
        };

        private static async Task<int> Main(string[] args)
        {
            bool checkOnly = (args.Length > 0) && (args[^1] == "--check");
            int directoryArguments = args.Length - (checkOnly ? 1 : 0);
            if (directoryArguments > 1)
            {
                await Console.Error.WriteLineAsync("Usage: dotnet run --file UpdateLicense.cs -- [directory] [--check]");
                return 1;
            }

            string root = Path.GetFullPath(directoryArguments == 1 ? args[0] : Directory.GetCurrentDirectory());
            int currentYear = DateTime.UtcNow.Year;
            string yearPart = currentYear <= StartYear ? $"{StartYear}" : $"{StartYear}–{currentYear}";
            string wantedLine1 = $"// SPDX-License-Identifier: {Spdx}";
            string wantedLine2 = $"// Copyright (c) {yearPart} {Author}";
            int updated = 0;

            foreach (string file in EnumerateSources(root))
            {
                if (await ProcessFileAsync(file, wantedLine1, wantedLine2, checkOnly))
                {
                    updated++;
                    string status = checkOnly ? "missing" : "updated";
                    Console.WriteLine($"[{status}] {Path.GetRelativePath(root, file)}");
                }
            }

            Console.WriteLine($"License headers requiring updates: {updated}.");
            return checkOnly && (updated > 0) ? 1 : 0;
        }

        private static IEnumerable<string> EnumerateSources(string directory)
        {
            foreach (string file in Directory.EnumerateFiles(directory, "*.cs"))
            {
                if (!file.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
                    && !file.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase)
                    && !file.EndsWith(".AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase))
                {
                    yield return file;
                }
            }

            foreach (string child in Directory.EnumerateDirectories(directory))
            {
                if (!ExcludedDirectories.Contains(Path.GetFileName(child)))
                {
                    foreach (string file in EnumerateSources(child))
                    {
                        yield return file;
                    }
                }
            }
        }

        private static async Task<bool> ProcessFileAsync(string filePath, string wantedLine1,
            string wantedLine2, bool checkOnly)
        {
            List<string> allLines = (await File.ReadAllLinesAsync(filePath, FileEncoding)).ToList();
            bool hasSpdxHeader = (allLines.Count > 0) && allLines[0].TrimStart()
                .StartsWith("// SPDX-License-Identifier:", StringComparison.Ordinal);
            bool changed = false;

            if (hasSpdxHeader)
            {
                if (!string.Equals(allLines[0], wantedLine1, StringComparison.Ordinal))
                {
                    allLines[0] = wantedLine1;
                    changed = true;
                }

                if ((allLines.Count < 2) || !allLines[1].TrimStart()
                    .StartsWith("// Copyright (c)", StringComparison.Ordinal))
                {
                    allLines.Insert(1, wantedLine2);
                    changed = true;
                }
                else if (!string.Equals(allLines[1], wantedLine2, StringComparison.Ordinal))
                {
                    allLines[1] = wantedLine2;
                    changed = true;
                }

                if ((allLines.Count < 3) || (allLines[2].Length != 0))
                {
                    allLines.Insert(2, string.Empty);
                    changed = true;
                }
            }
            else
            {
                allLines.InsertRange(0, [wantedLine1, wantedLine2, string.Empty]);
                changed = true;
            }

            if (changed && !checkOnly)
            {
                await File.WriteAllLinesAsync(filePath, allLines, FileEncoding);
            }

            return changed;
        }
    }
}
