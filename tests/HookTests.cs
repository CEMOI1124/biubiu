using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using Biubiu;

static class HookTests
{
    static int failures;

    public static int Run()
    {
        failures = 0;
        string previousTasksPath = Environment.GetEnvironmentVariable("BIUBIU_TASKS_PATH");
        try
        {
            Environment.SetEnvironmentVariable("BIUBIU_TASKS_PATH", "C:\\temp\\custom-tasks.json");
            if (TaskBoard.DefaultPath() != "C:\\temp\\custom-tasks.json")
                Fail("tasks path override was ignored");
        }
        finally
        {
            Environment.SetEnvironmentVariable("BIUBIU_TASKS_PATH", previousTasksPath);
        }

        ExpectPath("/d:/work/Biubiu", "d:\\work\\Biubiu");
        ExpectPath("D:/work/Biubiu/", "D:\\work\\Biubiu");
        TestInstaller();
        TestStale();
        TestScript();
        return failures;
    }

    static void ExpectPath(string input, string expected)
    {
        string actual = TaskBoard.NormalizeWorkspacePath(input);
        if (actual == expected)
            return;
        Fail("path " + input + " => " + actual + ", expected " + expected);
    }

    static void TestInstaller()
    {
        string root = Path.Combine(Path.GetTempPath(), "biubiu-hook-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string original = "{\r\n  \"version\": 1,\r\n  \"hooks\": {\r\n    \"beforeShellExecution\": [\r\n      { \"command\": \"./hooks/other.ps1\", \"failClosed\": true }\r\n    ]\r\n  }\r\n}\r\n";
            File.WriteAllText(Path.Combine(root, "hooks.json"), original, new UTF8Encoding(false));

            HookInstallResult first = HookInstaller.Install(root, "line-1\n");
            if (!first.Ok || !first.WroteHooks || !first.WroteScript)
                Fail("first install did not write hook files");
            AssertCommands(root, true);

            string backup = File.ReadAllText(Path.Combine(root, "hooks.json.biubiu-backup"), Encoding.UTF8);
            if (backup != original)
                Fail("backup does not match the original hooks.json");

            HookInstallResult second = HookInstaller.Install(root, "line-1\n");
            if (!second.Ok || second.WroteHooks || second.WroteScript)
                Fail("second install rewrote unchanged hook files");

            HookInstallResult third = HookInstaller.Install(root, "line-2\n");
            if (!third.Ok || !third.WroteScript || third.WroteHooks)
                Fail("script update also rewrote hooks.json");
            string script = File.ReadAllText(Path.Combine(root, "hooks", HookInstaller.ScriptFileName), Encoding.UTF8);
            if (script != "line-2\n")
                Fail("updated script text was not written");

            string brokenRoot = Path.Combine(Path.GetTempPath(), "biubiu-hook-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(brokenRoot);
            try
            {
                string broken = "{ this is not json";
                File.WriteAllText(Path.Combine(brokenRoot, "hooks.json"), broken, new UTF8Encoding(false));
                HookInstallResult brokenResult = HookInstaller.Install(brokenRoot, "line-1\n");
                string after = File.ReadAllText(Path.Combine(brokenRoot, "hooks.json"), Encoding.UTF8);
                if (brokenResult.Ok || brokenResult.WroteHooks || after != broken)
                    Fail("invalid hooks.json was modified");
            }
            finally
            {
                try { Directory.Delete(brokenRoot, true); } catch (IOException) { }
            }
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    static void AssertCommands(string root, bool otherFailClosed)
    {
        string json = File.ReadAllText(Path.Combine(root, "hooks.json"), Encoding.UTF8);
        JavaScriptSerializer serializer = new JavaScriptSerializer();
        Dictionary<string, object> document = (Dictionary<string, object>)serializer.DeserializeObject(json);
        Dictionary<string, object> hooks = (Dictionary<string, object>)document["hooks"];
        if (!HasCommand(hooks, "beforeSubmitPrompt", HookInstaller.HookCommand("beforeSubmitPrompt")))
            Fail("beforeSubmitPrompt command missing");
        if (!HasCommand(hooks, "stop", HookInstaller.HookCommand("stop")))
            Fail("stop command missing");
        if (!HasCommand(hooks, "beforeShellExecution", "./hooks/other.ps1"))
            Fail("existing hook was dropped");

        IList shellHooks = (IList)hooks["beforeShellExecution"];
        Dictionary<string, object> other = (Dictionary<string, object>)shellHooks[0];
        if (!(other["failClosed"] is bool) || (bool)other["failClosed"] != otherFailClosed)
            Fail("existing failClosed value changed");

        IList promptHooks = (IList)hooks["beforeSubmitPrompt"];
        Dictionary<string, object> ours = (Dictionary<string, object>)promptHooks[0];
        if (!(ours["failClosed"] is bool) || (bool)ours["failClosed"])
            Fail("installed hook is fail closed");
    }

    static bool HasCommand(Dictionary<string, object> hooks, string eventName, string command)
    {
        object value;
        if (!hooks.TryGetValue(eventName, out value) || !(value is IList))
            return false;
        IList list = (IList)value;
        for (int i = 0; i < list.Count; i++)
        {
            Dictionary<string, object> item = list[i] as Dictionary<string, object>;
            if (item == null)
                continue;
            object existing;
            if (item.TryGetValue("command", out existing) && existing != null && existing.ToString() == command)
                return true;
        }
        return false;
    }

    static void TestStale()
    {
        string path = Path.Combine(Path.GetTempPath(), "biubiu-tasks-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path,
                "{\"tasks\":["
                + "{\"conversationId\":\"old\",\"workspacePath\":\"/d:/work/Biubiu\",\"summary\":\"old task\",\"status\":\"running\",\"updatedAt\":\"2020-01-01T00:00:00.0000000+08:00\"},"
                + "{\"conversationId\":\"fresh\",\"workspacePath\":\"D:\\\\work\\\\Biubiu\",\"summary\":\"fresh task\",\"status\":\"running\",\"updatedAt\":\"2026-10-23T13:00:00.0000000+08:00\"},"
                + "{\"conversationId\":\"done\",\"workspacePath\":\"D:\\\\work\\\\Biubiu\",\"summary\":\"done task\",\"status\":\"completed\",\"updatedAt\":\"2020-01-01T00:00:00.0000000+08:00\"}"
                + "]}",
                new UTF8Encoding(false));
            TaskLoadResult loaded = TaskBoard.Load(path, DateTimeOffset.Parse("2026-10-23T13:05:00.0000000+08:00"));
            if (!loaded.Ok || loaded.Tasks.Count != 3)
            {
                Fail("task file did not load");
                return;
            }
            if (loaded.Tasks[0].Status != "unknown")
                Fail("stale running task stayed running");
            if (loaded.Tasks[1].Status != "running")
                Fail("fresh running task changed");
            if (loaded.Tasks[2].Status != "completed")
                Fail("completed task was marked unknown");
            if (loaded.Tasks[0].WorkspacePath != "d:\\work\\Biubiu")
                Fail("workspace path was not normalized: " + loaded.Tasks[0].WorkspacePath);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    static void TestScript()
    {
        string script = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "hooks", "biubiu-status.ps1"));
        if (!File.Exists(script))
        {
            Fail("hook script not found: " + script);
            return;
        }

        string tasks = Path.Combine(Path.GetTempPath(), "biubiu-script-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tasks);
        try
        {
            string prompt = "\u5b8c\u5584\u767b\u5f55 \"hi\"";
            Dictionary<string, object> start = new Dictionary<string, object>();
            start["conversation_id"] = "conv-1";
            start["prompt"] = prompt;
            start["workspace_roots"] = new string[] { "/d:/work/Biubiu" };
            start["user_email"] = "hidden@example.com";
            string startJson = new JavaScriptSerializer().Serialize(start);
            HookRun started = RunPiped(script, "beforeSubmitPrompt", startJson, tasks);
            if (started.TimedOut || started.ExitCode != 0 || started.Stdout.Trim() != "{\"continue\":true}")
                Fail("start hook output was " + started.Stdout.Trim() + " exit " + started.ExitCode);
            Dictionary<string, object> record = ReadTask(tasks, "conv-1");
            if (record == null || (string)record["status"] != "running" || (string)record["summary"] != prompt)
                Fail("start hook did not store the running task");
            if (record != null && (string)record["workspacePath"] != "d:\\work\\Biubiu")
                Fail("start hook stored path " + record["workspacePath"]);
            string startedAt = record == null ? "" : record["startedAt"] as string;
            if (string.IsNullOrEmpty(startedAt))
                Fail("start hook did not record the start time");
            if (File.ReadAllText(Path.Combine(tasks, "conv-1.json"), Encoding.UTF8).IndexOf("hidden@example.com", StringComparison.Ordinal) >= 0)
                Fail("task file stored the user email");

            Dictionary<string, object> stop = new Dictionary<string, object>();
            stop["conversation_id"] = "conv-1";
            stop["status"] = "completed";
            stop["workspace_roots"] = new string[] { "/d:/work/Biubiu" };
            HookRun stopped = RunPiped(script, "stop", new JavaScriptSerializer().Serialize(stop), tasks);
            if (stopped.TimedOut || stopped.ExitCode != 0 || stopped.Stdout.Trim() != "{}")
                Fail("stop hook output was " + stopped.Stdout.Trim());
            record = ReadTask(tasks, "conv-1");
            if (record == null || (string)record["status"] != "completed" || (string)record["summary"] != prompt)
                Fail("stop hook did not keep the summary");
            if (record == null || (string)record["startedAt"] != startedAt)
                Fail("stop hook replaced the start time");

            string taskFile = Path.Combine(tasks, "conv-1.json");
            string beforeInvalid = File.ReadAllText(taskFile, Encoding.UTF8);
            HookRun invalid = RunPiped(script, "beforeSubmitPrompt", "not-json", tasks);
            if (invalid.TimedOut || invalid.ExitCode != 0 || invalid.Stdout.Trim() != "{\"continue\":true}")
                Fail("invalid hook input blocked the response");
            if (File.ReadAllText(taskFile, Encoding.UTF8) != beforeInvalid)
                Fail("invalid hook input changed the task file");

            string capped = Path.Combine(Path.GetTempPath(), "biubiu-cap-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(capped);
            HookRun cap = RunCap(script, capped);
            if (cap.TimedOut || cap.ExitCode != 0)
                Fail("cap run failed");
            TaskLoadResult loaded = TaskBoard.Load(capped, DateTimeOffset.Now);
            if (!loaded.Ok || loaded.Tasks.Count != 50)
                Fail("task file kept " + (loaded.Ok ? loaded.Tasks.Count.ToString() : "invalid") + " records");
            if (Directory.Exists(capped))
                Directory.Delete(capped, true);
        }
        finally
        {
            if (Directory.Exists(tasks))
                Directory.Delete(tasks, true);
        }
    }

    static HookRun RunPiped(string script, string eventName, string payload, string tasksPath)
    {
        string runner = Path.Combine(Path.GetTempPath(), "biubiu-runner-" + Guid.NewGuid().ToString("N") + ".ps1");
        string payloadPath = runner + ".json";
        File.WriteAllText(payloadPath, payload, new UTF8Encoding(true));
        File.WriteAllText(runner, "$payload = Get-Content -LiteralPath $env:BIUBIU_TEST_PAYLOAD -Raw -Encoding UTF8\r\n$payload | & $env:BIUBIU_TEST_SCRIPT $env:BIUBIU_TEST_EVENT\r\n", new UTF8Encoding(false));
        try
        {
            ProcessStartInfo info = NewProcess("-NoProfile -ExecutionPolicy Bypass -File \"" + runner + "\"");
            info.EnvironmentVariables["BIUBIU_TASKS_PATH"] = tasksPath;
            info.EnvironmentVariables["BIUBIU_TEST_PAYLOAD"] = payloadPath;
            info.EnvironmentVariables["BIUBIU_TEST_SCRIPT"] = script;
            info.EnvironmentVariables["BIUBIU_TEST_EVENT"] = eventName;
            return Run(info, null);
        }
        finally
        {
            File.Delete(runner);
            File.Delete(payloadPath);
        }
    }

    static HookRun RunCap(string script, string tasksPath)
    {
        string runner = Path.Combine(Path.GetTempPath(), "biubiu-cap-" + Guid.NewGuid().ToString("N") + ".ps1");
        File.WriteAllText(runner,
            "$script = $env:BIUBIU_TEST_SCRIPT\r\nforeach ($n in 1..51) {\r\n  $payload = '{\"conversation_id\":\"cap' + $n + '\",\"prompt\":\"p\",\"workspace_roots\":[\"D:\\\\w\"]}'\r\n  $payload | & $script beforeSubmitPrompt | Out-Null\r\n}\r\n",
            new UTF8Encoding(false));
        try
        {
            ProcessStartInfo info = NewProcess("-NoProfile -ExecutionPolicy Bypass -File \"" + runner + "\"");
            info.EnvironmentVariables["BIUBIU_TASKS_PATH"] = tasksPath;
            info.EnvironmentVariables["BIUBIU_TEST_SCRIPT"] = script;
            return Run(info, null);
        }
        finally
        {
            File.Delete(runner);
        }
    }

    static ProcessStartInfo NewProcess(string arguments)
    {
        ProcessStartInfo info = new ProcessStartInfo();
        info.FileName = "powershell.exe";
        info.Arguments = arguments;
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.StandardOutputEncoding = new UTF8Encoding(false);
        info.StandardErrorEncoding = new UTF8Encoding(false);
        return info;
    }

    static HookRun Run(ProcessStartInfo info, string stdin)
    {
        HookRun run = new HookRun();
        Process process = Process.Start(info);
        if (stdin != null)
        {
            process.StandardInput.Write(stdin);
            process.StandardInput.Close();
        }
        run.Stdout = process.StandardOutput.ReadToEnd();
        run.Stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(60000))
        {
            try { process.Kill(); } catch (Exception) { }
            run.TimedOut = true;
            return run;
        }
        run.ExitCode = process.ExitCode;
        if (run.ExitCode != 0 && !string.IsNullOrEmpty(run.Stderr))
            Console.Error.WriteLine(run.Stderr);
        return run;
    }

    static Dictionary<string, object> ReadTask(string directory, string conversationId)
    {
        string path = Path.Combine(directory, conversationId + ".json");
        if (!File.Exists(path))
            return null;
        JavaScriptSerializer serializer = new JavaScriptSerializer();
        return (Dictionary<string, object>)serializer.DeserializeObject(File.ReadAllText(path, Encoding.UTF8));
    }

    static void Fail(string message)
    {
        failures++;
        Console.Error.WriteLine(message);
    }

    sealed class HookRun
    {
        public int ExitCode;
        public string Stdout = "";
        public string Stderr = "";
        public bool TimedOut;
    }
}
