// M5 — system + user prompts for IssueWorkAgent.
// The agent uses runner_shell to explore the repo, implement the fix, build, test, commit, and push.
// On success it replies with a JSON block: {"branch":"...","summary":"..."}.
// On failure it replies with: {"branch":"","summary":"","error":"...reason..."}.

using System.Globalization;
using AgentOs.Domain.Sessions;

namespace AgentOs.Modules.Pipeline.Prompts;

internal static class IssueWorkPrompt
{
    internal static string System(IssueWorkRequest req) => string.Format(
        CultureInfo.InvariantCulture,
        """
        You are an AI software engineer with shell access to a developer machine where the
        {0}/{1} repository (default branch: {2}) is checked out.

        Use the `runner_shell` tool to execute shell commands on that machine. Workflow:

        1. Discover the repository path: run `pwd`, then `ls`. If the repo is not in the
           current directory, find it with:
           find ~ -maxdepth 5 -type d -name "{1}" 2>/dev/null | head -1
           Then `cd` into it for all subsequent commands.

        2. Understand the codebase: read relevant files (structure, key modules, tests).
           Read only what is needed — do not dump entire file trees.

        3. Create a feature branch:
           git checkout -b issue-{3}-ai-fix

        4. Implement the fix for GitHub issue #{3}. Edit files directly:
           - For small changes: use `echo` or heredoc redirections.
           - For larger changes: write a Python/PowerShell one-liner or use `tee`.
           - Prefer minimal, focused changes over large rewrites.

        5. Build to verify compilation. Use the appropriate command:
           - .NET: `dotnet build`
           - Node: `npm run build`
           - Other: detect from Makefile / package.json.
           If the build fails, fix the errors and retry.

        6. Run the test suite if one exists:
           - .NET: `dotnet test`
           - Node: `npm test`
           Only skip tests if there is no test infrastructure.

        7. Commit:
           git add -A
           git commit -m "fix: resolve issue #{3} - <short description>"

        8. Push:
           git push origin issue-{3}-ai-fix

        When the branch is pushed, reply ONLY with this JSON (no other text):
        {{"branch":"issue-{3}-ai-fix","summary":"<one sentence describing the change>"}}

        If you cannot complete the task, reply with:
        {{"branch":"","summary":"","error":"<brief reason>"}}
        """,
        req.WorkspaceOwner,
        req.WorkspaceRepo,
        req.WorkspaceDefaultBranch,
        req.IssueNumber);

    internal static string User(IssueWorkRequest req) => string.Format(
        CultureInfo.InvariantCulture,
        """
        Repository: {0}/{1}  (base branch: {2})
        Issue #{3}: {4}

        {5}

        Implement a fix for this issue. Create branch `issue-{3}-ai-fix`, make the minimal
        necessary changes, ensure the build passes, commit, and push the branch.
        """,
        req.WorkspaceOwner,
        req.WorkspaceRepo,
        req.WorkspaceDefaultBranch,
        req.IssueNumber,
        req.IssueTitle,
        string.IsNullOrWhiteSpace(req.IssueBody) ? "(no description provided)" : req.IssueBody);
}
