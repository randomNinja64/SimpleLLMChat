using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace SkillTools
{
    internal static class SkillHandler
    {
        private const int MaxDescriptionLength = 1024;
        private const int MaxNameLength = 64;
        private static readonly Regex SkillNameRegex = new Regex(
            @"^[a-z0-9]+(-[a-z0-9]+)*$",
            RegexOptions.Compiled);

        private sealed class SkillInfo
        {
            public string FolderName;
            public string DirectoryPath;
            public string SkillMdPath;
            public string Description;
        }

        private static string GetSkillsDirectory()
        {
            string configured = ToolHelper.GetConfigValue("skillsDirectory").Trim();
            if (!string.IsNullOrEmpty(configured))
                return Path.GetFullPath(Environment.ExpandEnvironmentVariables(configured));

            string baseDir = Path.GetDirectoryName(
                System.Reflection.Assembly.GetEntryAssembly()?.Location
                ?? typeof(SkillHandler).Assembly.Location);
            return Path.GetFullPath(Path.Combine(baseDir, "skills"));
        }

        public static string GetContext()
        {
            List<SkillInfo> skills = DiscoverSkills();
            var sb = new StringBuilder();
            sb.AppendLine("Skills:");
            sb.AppendLine("Skills are located in " + GetSkillsDirectory() + ".");
            sb.AppendLine("Before replying, scan the skills below. If one clearly matches your task, load it with view_skill and follow its instructions.");
            sb.AppendLine("To run a skill's scripts, run them as shell commands under the skill root returned by view_skill.");
            sb.AppendLine("To author or change skills, use create_skill, edit_skill, edit_skill_file, or remove_skill if available.");
            sb.AppendLine();

            if (skills.Count == 0)
            {
                sb.AppendLine("(no skills installed)");
                return sb.ToString().Trim();
            }

            foreach (SkillInfo skill in skills)
                sb.AppendLine(skill.FolderName + ": " + TruncateDescription(skill.Description, out _));
            return sb.ToString().Trim();
        }

        public static string ViewSkill(string name, string relativePath)
        {
            SkillInfo skill = FindSkill(name);
            if (skill == null)
                throw new InvalidOperationException("skill does not exist: " + name);

            if (!string.IsNullOrWhiteSpace(relativePath))
                return ReadLinkedFile(skill, relativePath.Trim());

            string skillMd = File.ReadAllText(skill.SkillMdPath, Encoding.UTF8);
            var sb = new StringBuilder();
            sb.AppendLine("Skill root: " + skill.DirectoryPath);
            sb.AppendLine("Linked files:");
            ListLinkedFiles(skill.DirectoryPath, skill.DirectoryPath, sb);
            sb.AppendLine();
            sb.AppendLine("--- SKILL.md ---");
            sb.Append(skillMd);
            return sb.ToString().TrimEnd();
        }

        public static string CreateSkill(string name, string description, string instructions)
        {
            name = (name ?? "").Trim();
            description = (description ?? "").Trim();
            instructions = instructions ?? "";

            ValidateNewSkillName(name);
            if (string.IsNullOrEmpty(description))
                throw new ArgumentException("description is required.");
            if (string.IsNullOrWhiteSpace(instructions))
                throw new ArgumentException("instructions is required.");

            bool descriptionTruncated;
            description = TruncateDescription(description, out descriptionTruncated);

            SkillInfo existing = FindSkill(name);
            if (existing != null)
                throw new InvalidOperationException("skill already exists: " + name + " (" + existing.DirectoryPath + ")");

            string root = GetSkillsDirectory();
            if (!Directory.Exists(root))
                Directory.CreateDirectory(root);

            string skillDir = Path.Combine(root, name);
            if (Directory.Exists(skillDir))
                throw new InvalidOperationException("skill already exists: " + name + " (" + Path.GetFullPath(skillDir) + ")");

            Directory.CreateDirectory(skillDir);
            string content = BuildSkillMarkdown(name, description, instructions);
            string skillRoot = Path.GetFullPath(skillDir);
            File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), content, Encoding.UTF8);
            string result = "Skill created: " + name
                + "\nSkill root: " + skillRoot
                + "\nSKILL.md was written. To add scripts, references, or other supporting files, use edit_skill_file"
                + " (e.g. relative_path 'scripts/helper.py' or 'references/api.md').";
            if (descriptionTruncated)
                result += "\nNote: description truncated to " + MaxDescriptionLength + " characters.";
            return result;
        }

        public static string EditSkill(string name, string description, string instructions)
        {
            name = (name ?? "").Trim();
            bool updateDescription = !string.IsNullOrWhiteSpace(description);
            bool updateInstructions = !string.IsNullOrWhiteSpace(instructions);

            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("name is required.");
            if (!updateDescription && !updateInstructions)
                throw new ArgumentException("provide description and/or instructions to update.");

            SkillInfo existing = FindSkill(name);
            if (existing == null)
                throw new InvalidOperationException("skill does not exist: " + name);

            string existingDescription;
            string existingBody;
            ParseSkillMarkdown(
                File.ReadAllText(existing.SkillMdPath, Encoding.UTF8),
                out existingDescription,
                out existingBody);

            bool descriptionTruncated = false;
            string newDescription = existingDescription;
            if (updateDescription)
                newDescription = TruncateDescription(description.Trim(), out descriptionTruncated);
            string newInstructions = updateInstructions ? instructions : existingBody;

            // Keep frontmatter name aligned with the folder identity
            string content = BuildSkillMarkdown(existing.FolderName, newDescription, newInstructions);
            File.WriteAllText(existing.SkillMdPath, content, Encoding.UTF8);

            var updated = new List<string>();
            if (updateDescription)
                updated.Add("description");
            if (updateInstructions)
                updated.Add("instructions");
            string result = "Skill updated: " + existing.FolderName + " (" + string.Join(", ", updated.ToArray()) + ")"
                + "\nSkill root: " + existing.DirectoryPath;
            if (descriptionTruncated)
                result += "\nNote: description truncated to " + MaxDescriptionLength + " characters.";
            return result;
        }

        public static string EditSkillFile(string name, string relativePath, string content)
        {
            name = (name ?? "").Trim();
            relativePath = (relativePath ?? "").Trim();
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("name is required.");
            if (string.IsNullOrEmpty(relativePath))
                throw new ArgumentException("relative_path is required.");
            if (content == null)
                throw new ArgumentException("content is required.");

            SkillInfo existing = FindSkill(name);
            if (existing == null)
                throw new InvalidOperationException("skill does not exist: " + name);

            string full = ResolveSkillRelativePath(existing, relativePath, out string displayRelative);
            if (string.Equals(Path.GetFileName(displayRelative), "SKILL.md", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("cannot modify SKILL.md via edit_skill_file.");

            string parent = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                Directory.CreateDirectory(parent);

            File.WriteAllText(full, content, Encoding.UTF8);
            return "Skill file written: " + displayRelative
                + "\nSkill root: " + existing.DirectoryPath;
        }

        private static void ValidateNewSkillName(string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("name is required.");
            if (name.Length > MaxNameLength)
                throw new ArgumentException("name must be " + MaxNameLength + " characters or fewer.");
            if (!SkillNameRegex.IsMatch(name))
                throw new ArgumentException("name must use lowercase letters, numbers, and hyphens only (e.g. 'code-review').");
        }

        public static string RemoveSkill(string name)
        {
            name = (name ?? "").Trim();
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("name is required.");

            SkillInfo skill = FindSkill(name);
            if (skill == null)
                throw new InvalidOperationException("skill does not exist: " + name);

            Directory.Delete(skill.DirectoryPath, true);
            return "Skill removed: " + name;
        }

        private static List<SkillInfo> DiscoverSkills()
        {
            var results = new List<SkillInfo>();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string root = GetSkillsDirectory();
            if (!Directory.Exists(root))
                return results;

            foreach (string skillMd in Directory.GetFiles(root, "SKILL.md", SearchOption.AllDirectories))
            {
                string dir = Path.GetDirectoryName(skillMd);
                if (string.IsNullOrEmpty(dir))
                    continue;

                string folderName = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(folderName) || !seenNames.Add(folderName))
                    continue; // first path wins on duplicate folder names

                string description = "";
                try
                {
                    string body;
                    ParseSkillMarkdown(File.ReadAllText(skillMd, Encoding.UTF8), out description, out body);
                }
                catch
                {
                    // Keep skill discoverable even if frontmatter is broken
                }

                if (string.IsNullOrWhiteSpace(description))
                    description = "(no description)";

                results.Add(new SkillInfo
                {
                    FolderName = folderName,
                    DirectoryPath = Path.GetFullPath(dir),
                    SkillMdPath = Path.GetFullPath(skillMd),
                    Description = description.Trim()
                });
            }

            results.Sort((a, b) => string.Compare(a.FolderName, b.FolderName, StringComparison.OrdinalIgnoreCase));
            return results;
        }

        private static SkillInfo FindSkill(string name)
        {
            name = (name ?? "").Trim();
            if (string.IsNullOrEmpty(name))
                return null;

            foreach (SkillInfo skill in DiscoverSkills())
            {
                if (string.Equals(skill.FolderName, name, StringComparison.OrdinalIgnoreCase))
                    return skill;
            }
            return null;
        }

        private static string ResolveSkillRelativePath(SkillInfo skill, string relativePath, out string displayRelative)
        {
            relativePath = relativePath.Trim().Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(relativePath))
                throw new ArgumentException("path must be a relative path within the skill (no absolute paths).");

            string[] segments = relativePath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
                throw new ArgumentException("path must be a relative path within the skill.");
            foreach (string segment in segments)
            {
                if (segment == "." || segment == "..")
                    throw new ArgumentException("path must be a relative path within the skill (no '..' segments).");
            }

            string full = Path.GetFullPath(Path.Combine(skill.DirectoryPath, relativePath));
            string root = skill.DirectoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(full, skill.DirectoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("path must stay within the skill directory.");
            }

            displayRelative = string.Join("/", segments);
            return full;
        }

        private static string ReadLinkedFile(SkillInfo skill, string relativePath)
        {
            string full = ResolveSkillRelativePath(skill, relativePath, out string displayRelative);
            if (!File.Exists(full))
                throw new InvalidOperationException(
                    "file not found in skill '" + skill.FolderName + "': " + displayRelative);

            var sb = new StringBuilder();
            sb.AppendLine("Skill root: " + skill.DirectoryPath);
            sb.AppendLine("File: " + displayRelative);
            sb.AppendLine("---");
            sb.Append(File.ReadAllText(full, Encoding.UTF8));
            return sb.ToString().TrimEnd();
        }

        private static void ListLinkedFiles(string skillRoot, string currentDir, StringBuilder sb)
        {
            string[] entries;
            try
            {
                entries = Directory.GetFileSystemEntries(currentDir);
            }
            catch
            {
                return;
            }

            Array.Sort(entries, StringComparer.OrdinalIgnoreCase);
            foreach (string entry in entries)
            {
                string name = Path.GetFileName(entry);
                if (string.Equals(name, "SKILL.md", StringComparison.OrdinalIgnoreCase))
                    continue;

                string relative = entry.Substring(skillRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Replace(Path.DirectorySeparatorChar, '/');

                if (Directory.Exists(entry))
                {
                    sb.AppendLine("  " + relative + "/");
                    ListLinkedFiles(skillRoot, entry, sb);
                }
                else
                {
                    sb.AppendLine("  " + relative);
                }
            }
        }

        private static string TruncateDescription(string description, out bool truncated)
        {
            truncated = false;
            if (string.IsNullOrEmpty(description))
                return "";
            description = description.Trim();
            if (description.Length <= MaxDescriptionLength)
                return description;
            truncated = true;
            const string marker = "...truncated";
            int keep = MaxDescriptionLength - marker.Length;
            if (keep < 1)
                return marker.Substring(0, Math.Min(marker.Length, MaxDescriptionLength));
            return description.Substring(0, keep) + marker;
        }

        private static string BuildSkillMarkdown(string name, string description, string instructions)
        {
            var sb = new StringBuilder();
            sb.AppendLine("---");
            sb.AppendLine("name: " + name);
            sb.AppendLine("description: " + QuoteYamlScalar(description));
            sb.AppendLine("---");
            sb.AppendLine();
            string body = instructions.Trim();
            if (!string.IsNullOrEmpty(body))
                sb.Append(body);
            if (!body.EndsWith("\n") && !string.IsNullOrEmpty(body))
                sb.AppendLine();
            return sb.ToString();
        }

        private static string QuoteYamlScalar(string value)
        {
            if (value.IndexOfAny(new[] { ':', '#', '"', '\'', '\n', '\r', '{', '}', '[', ']', ',', '&', '*', '!', '|', '>', '%' }) >= 0
                || value != value.Trim()
                || value.Length == 0)
            {
                return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            }
            return value;
        }

        /// <summary>
        /// Parses YAML frontmatter and returns the markdown body after the closing --- fence.
        /// If frontmatter is missing or malformed, body is the full content and description stays empty.
        /// Skill identity is the folder name; YAML name is ignored if present.
        /// </summary>
        internal static void ParseSkillMarkdown(string content, out string description, out string body)
        {
            description = "";
            body = content ?? "";
            if (string.IsNullOrEmpty(content))
                return;

            string normalized = content.Replace("\r\n", "\n").Replace("\r", "\n");
            if (!normalized.StartsWith("---", StringComparison.Ordinal))
                return;

            int end = normalized.IndexOf("\n---", 3, StringComparison.Ordinal);
            if (end < 0)
                return;

            string yaml = normalized.Substring(3, end - 3).Trim('\n');
            string[] lines = yaml.Split(new[] { '\n' }, StringSplitOptions.None);
            foreach (string rawLine in lines)
            {
                string line = rawLine.TrimEnd();
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
                    continue;

                // Skip indented nested keys
                if (char.IsWhiteSpace(line[0]))
                    continue;

                int colon = line.IndexOf(':');
                if (colon <= 0)
                    continue;

                string key = line.Substring(0, colon).Trim();
                if (!string.Equals(key, "description", StringComparison.OrdinalIgnoreCase))
                    continue;

                string value = line.Substring(colon + 1).Trim();
                description = UnquoteYamlScalar(value);
            }

            body = normalized.Substring(end + 4); // after "\n---"
            if (body.StartsWith("\n"))
                body = body.Substring(1);
        }

        private static string UnquoteYamlScalar(string value)
        {
            if (value.Length >= 2)
            {
                if ((value[0] == '"' && value[value.Length - 1] == '"')
                    || (value[0] == '\'' && value[value.Length - 1] == '\''))
                {
                    value = value.Substring(1, value.Length - 2);
                    value = value.Replace("\\\"", "\"").Replace("\\\\", "\\");
                }
            }
            return value;
        }
    }
}
