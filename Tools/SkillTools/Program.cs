using System;

namespace SkillTools
{
    internal class Program
    {
        static int Main(string[] args)
        {
            return ToolHelper.RunToolMain(args, (toolName, argumentsJson) =>
            {
                switch (toolName)
                {
                    case "view_skill":
                        {
                            string name = ToolHelper.GetRequiredArg(argumentsJson, "name");
                            string relativePath = ToolHelper.JsonExtractString(argumentsJson, "relative_path")?.Trim() ?? "";
                            return new ToolResult(SkillHandler.ViewSkill(name, relativePath), 0);
                        }

                    case "create_skill":
                        {
                            string name = ToolHelper.GetRequiredArg(argumentsJson, "name");
                            string description = ToolHelper.GetRequiredArg(argumentsJson, "description");
                            string instructions = ToolHelper.GetRequiredArg(argumentsJson, "instructions");
                            return new ToolResult(SkillHandler.CreateSkill(name, description, instructions), 0);
                        }

                    case "edit_skill":
                        {
                            string name = ToolHelper.GetRequiredArg(argumentsJson, "name");
                            string description = ToolHelper.JsonExtractString(argumentsJson, "description")?.Trim() ?? "";
                            string instructions = ToolHelper.JsonExtractString(argumentsJson, "instructions") ?? "";
                            return new ToolResult(SkillHandler.EditSkill(name, description, instructions), 0);
                        }

                    case "edit_skill_file":
                        {
                            string name = ToolHelper.GetRequiredArg(argumentsJson, "name");
                            string relativePath = ToolHelper.GetRequiredArg(argumentsJson, "relative_path");
                            string content = ToolHelper.JsonExtractString(argumentsJson, "content");
                            if (content == null)
                                throw new ArgumentException("missing 'content' argument.");
                            return new ToolResult(SkillHandler.EditSkillFile(name, relativePath, content), 0);
                        }

                    case "remove_skill":
                        {
                            string name = ToolHelper.GetRequiredArg(argumentsJson, "name");
                            return new ToolResult(SkillHandler.RemoveSkill(name), 0);
                        }

                    case "get_skills_context":
                        return new ToolResult(SkillHandler.GetContext() ?? "", 0);

                    default:
                        return new ToolResult("error: unknown tool '" + toolName + "'.", 1);
                }
            });
        }
    }
}
