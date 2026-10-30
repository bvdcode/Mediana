// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Xml.Linq;

namespace Mediana.Tests
{
    public class LibraryArchitectureTests
    {
        private static string Root
        {
            get
            {
                DirectoryInfo? directory = new(AppContext.BaseDirectory);
                while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Mediana.slnx")))
                {
                    directory = directory.Parent;
                }
                return directory?.FullName ?? throw new DirectoryNotFoundException("The solution root was not found.");
            }
        }

        private static Dictionary<string, string[]> Dependencies() => Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.csproj", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetFileNameWithoutExtension(path), path => XDocument.Load(path).Descendants("ProjectReference")
                .Select(reference => Path.GetFileNameWithoutExtension(reference.Attribute("Include")!.Value)).ToArray(), StringComparer.Ordinal);

        [Fact]
        public void LibraryDependencyGraphIsAcyclicAndDoesNotReferenceApplications()
        {
            Dictionary<string, string[]> graph = Dependencies();
            HashSet<string> visiting = [];
            HashSet<string> visited = [];
            foreach (string project in graph.Keys)
            {
                Visit(project);
            }
            void Visit(string project)
            {
                Assert.True(graph.ContainsKey(project), $"Library references a non-library project: {project}.");
                if (visited.Contains(project))
                {
                    return;
                }
                Assert.True(visiting.Add(project), $"Dependency cycle at {project}.");
                foreach (string dependency in graph[project])
                {
                    Visit(dependency);
                }
                visiting.Remove(project);
                visited.Add(project);
            }
        }

        [Fact]
        public void FoundationAndFormatBoundariesArePreserved()
        {
            Dictionary<string, string[]> graph = Dependencies();
            Assert.Empty(graph["Mediana.Core"]);
            foreach (string project in new[] { "Mediana.IO", "Mediana.Codecs", "Mediana.Formats", "Mediana.Drawing", "Mediana.Processing" })
            {
                Assert.All(graph[project], dependency => Assert.Equal("Mediana.Core", dependency));
            }
            Assert.All(graph.Where(pair => pair.Key.StartsWith("Mediana.Containers", StringComparison.Ordinal)), pair =>
                Assert.DoesNotContain(pair.Value, dependency => dependency.StartsWith("Mediana.Codecs", StringComparison.Ordinal)));
            Assert.All(graph.Where(pair => pair.Key.StartsWith("Mediana.Codecs.", StringComparison.Ordinal)), pair =>
                Assert.DoesNotContain(pair.Value, dependency => dependency.StartsWith("Mediana.Containers", StringComparison.Ordinal) || dependency == "Mediana.IO"));
            Assert.DoesNotContain("Mediana.Images.Svg", graph["Mediana.Documents.Pdf"]);
            Assert.DoesNotContain("Mediana.Images.Svg", graph["Mediana.Images.Tgs"]);
        }

        [Fact]
        public void EveryLibraryIsPartOfTheSolutionAndPackaged()
        {
            XDocument solution = XDocument.Load(Path.Combine(Root, "Mediana.slnx"));
            HashSet<string> projects = solution.Descendants("Project").Select(project => project.Attribute("Path")!.Value).ToHashSet(StringComparer.Ordinal);
            XDocument defaults = XDocument.Load(Path.Combine(Root, "src", "Directory.Build.props"));
            Assert.Equal("true", defaults.Descendants("IsPackable").Single().Value);
            foreach (string path in Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.csproj", SearchOption.AllDirectories))
            {
                Assert.Contains(Path.GetRelativePath(Root, path).Replace('\\', '/'), projects);
                Assert.DoesNotContain(XDocument.Load(path).Descendants("IsPackable"), property => property.Value != "true");
            }
        }
    }
}
